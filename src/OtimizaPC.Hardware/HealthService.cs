using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using OtimizaPC.Core;
using OtimizaPC.Core.Models;
using OtimizaPC.Hardware.Interop;
using OtimizaPC.Hardware.Sensors;

namespace OtimizaPC.Hardware
{
    /// <summary>Verificações rápidas de saúde do sistema que alimentam a nota do painel.</summary>
    public sealed class HealthService
    {
        private const int WarningPenalty = 10;
        private const int CriticalPenalty = 25;

        private readonly SensorService _sensors;

        public HealthService(SensorService sensors)
        {
            _sensors = sensors;
        }

        public Task<HealthResult> RunAsync() => Task.Run(() => Run());

        private HealthResult Run()
        {
            var result = new HealthResult();
            var checks = new Func<HealthCheck>[]
            {
                CheckSystemDrive,
                CheckDisks,
                CheckTemperature,
                CheckUptime,
                CheckPendingReboot,
                CheckStartup,
                CheckTempFiles,
                CheckPrivileges,
            };

            foreach (var check in checks)
            {
                try
                {
                    var item = check();
                    if (item != null) result.Checks.Add(item);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Verificação {check.Method.Name} falhou: {ex.Message}");
                }
            }

            var penalty = result.Checks.Sum(c =>
                c.Status == HealthStatus.Critical ? CriticalPenalty : c.Status == HealthStatus.Warning ? WarningPenalty : 0);
            result.Score = Math.Max(0, 100 - penalty);
            return result;
        }

        private static HealthCheck CheckSystemDrive()
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
            var freePercent = drive.TotalFreeSpace * 100.0 / drive.TotalSize;
            var detail = $"{Format.Bytes(drive.TotalFreeSpace)} livres ({Format.Percent(freePercent)}) em {drive.Name}";
            var status = freePercent < 10 ? HealthStatus.Critical : freePercent < 20 ? HealthStatus.Warning : HealthStatus.Ok;
            return new HealthCheck("Espaço no disco do sistema", detail, status);
        }

        private static HealthCheck CheckDisks()
        {
            var disks = Wmi.Query("SELECT FriendlyName, HealthStatus FROM MSFT_PhysicalDisk", Wmi.Storage);
            if (disks.Count == 0) return null;

            var failing = disks.Where(d => d.Int("HealthStatus") == 2).Select(d => d.Str("FriendlyName")).ToList();
            var warning = disks.Where(d => d.Int("HealthStatus") == 1).Select(d => d.Str("FriendlyName")).ToList();
            if (failing.Count > 0)
                return new HealthCheck("Saúde dos discos", "Com falha: " + string.Join(", ", failing) + ". Faça backup imediatamente!", HealthStatus.Critical);
            if (warning.Count > 0)
                return new HealthCheck("Saúde dos discos", "Atenção: " + string.Join(", ", warning), HealthStatus.Warning);
            return new HealthCheck("Saúde dos discos", $"{disks.Count} disco(s) saudável(is)", HealthStatus.Ok);
        }

        private HealthCheck CheckTemperature()
        {
            var temperature = SensorPicker.CpuTemperature(_sensors.Read());
            if (!temperature.HasValue)
            {
                return new HealthCheck("Temperatura da CPU",
                    "Indisponível. Requer administrador e o driver PawnIO instalado.", HealthStatus.Info);
            }

            var text = SensorReading.FormatValue(SensorKind.Temperature, temperature);
            if (temperature >= 90) return new HealthCheck("Temperatura da CPU", text + " — muito alta, verifique a refrigeração", HealthStatus.Critical);
            if (temperature >= 80) return new HealthCheck("Temperatura da CPU", text + " — elevada", HealthStatus.Warning);
            return new HealthCheck("Temperatura da CPU", text, HealthStatus.Ok);
        }

        private static HealthCheck CheckUptime()
        {
            var uptime = SystemMetrics.GetUptime();
            var detail = "Ligado há " + Format.Duration(uptime);
            return uptime.TotalDays >= 14
                ? new HealthCheck("Tempo ligado", detail + ". Reiniciar ajuda a liberar recursos.", HealthStatus.Warning)
                : new HealthCheck("Tempo ligado", detail, HealthStatus.Ok);
        }

        private static HealthCheck CheckPendingReboot() =>
            WindowsInfo.RebootPending
                ? new HealthCheck("Atualizações", "Reinicialização pendente para concluir atualizações", HealthStatus.Warning)
                : null;

        private static HealthCheck CheckStartup()
        {
            var count = StartupCount();
            var detail = $"{count} programa(s) iniciam com o Windows";
            return count > 15
                ? new HealthCheck("Inicialização", detail + " — isso deixa o boot lento", HealthStatus.Warning)
                : new HealthCheck("Inicialização", detail, HealthStatus.Ok);
        }

        private static int StartupCount()
        {
            const string run = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            const string run32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
            var count = Reg.ValueCount(RegistryHive.CurrentUser, run)
                        + Reg.ValueCount(RegistryHive.LocalMachine, run)
                        + Reg.ValueCount(RegistryHive.LocalMachine, run32);

            foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
            {
                try
                {
                    var path = Environment.GetFolderPath(folder);
                    if (Directory.Exists(path))
                        count += Directory.GetFiles(path).Count(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    // Pasta inacessível: ignora
                }
            }
            return count;
        }

        private static HealthCheck CheckTempFiles()
        {
            var windowsTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            var size = DirectorySize(Path.GetTempPath()) + DirectorySize(windowsTemp);
            var detail = $"{Format.Bytes(size)} em arquivos temporários. Use o módulo Limpeza para liberar.";
            return new HealthCheck("Arquivos temporários", detail, size > 2L * 1024 * 1024 * 1024 ? HealthStatus.Warning : HealthStatus.Info);
        }

        private static HealthCheck CheckPrivileges() =>
            AppInfo.IsAdministrator
                ? null
                : new HealthCheck("Permissões", "Executando sem administrador: algumas leituras ficam limitadas", HealthStatus.Info);

        /// <summary>Soma o tamanho de uma pasta, ignorando itens inacessíveis e links simbólicos.</summary>
        internal static long DirectorySize(string root, int maxFiles = 200_000)
        {
            long total = 0;
            var files = 0;
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0 && files < maxFiles)
            {
                var dir = pending.Pop();
                try
                {
                    var info = new DirectoryInfo(dir);
                    foreach (var file in info.EnumerateFiles())
                    {
                        total += file.Length;
                        if (++files >= maxFiles) break;
                    }
                    foreach (var sub in info.EnumerateDirectories())
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                            pending.Push(sub.FullName);
                    }
                }
                catch
                {
                    // Sem permissão ou pasta removida durante a varredura
                }
            }
            return total;
        }
    }
}
