using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using OtimizaPC.Core;
using OtimizaPC.Core.Models;
using OtimizaPC.Hardware.Interop;

namespace OtimizaPC.Hardware
{
    /// <summary>Uso de um processador lógico (1 item por thread da CPU).</summary>
    public sealed class CpuCoreUsage : INotifyPropertyChanged
    {
        private double _usage;

        public CpuCoreUsage(int index)
        {
            Index = index;
        }

        public int Index { get; }

        public string Label => "CPU " + (Index + 1);

        public double Usage
        {
            get => _usage;
            private set
            {
                if (Math.Abs(_usage - value) < 0.05) return;
                _usage = value;
                Raise(nameof(Usage));
                Raise(nameof(UsageText));
                Raise(nameof(Status));
            }
        }

        public string UsageText => Format.Percent(_usage);

        public HealthStatus Status =>
            _usage >= 90 ? HealthStatus.Critical : _usage >= 75 ? HealthStatus.Warning : HealthStatus.Ok;

        internal void Update(double usage) => Usage = Clamp(usage);

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static double Clamp(double value) => double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(100, value));
    }

    /// <summary>Consumo agregado de um processo (ou de processos com o mesmo nome).</summary>
    public sealed class ProcessUsageItem
    {
        public ProcessUsageItem(string name, double cpuPercent, long memoryBytes, ulong totalMemory)
        {
            Name = name;
            CpuPercent = double.IsNaN(cpuPercent) ? 0 : Math.Max(0, Math.Min(100, cpuPercent));
            MemoryBytes = Math.Max(0, memoryBytes);

            var memoryPercent = totalMemory == 0 ? 0 : MemoryBytes * 100.0 / totalMemory;
            var cpuStatus = CpuPercent >= 80 ? HealthStatus.Critical : CpuPercent >= 50 ? HealthStatus.Warning : HealthStatus.Ok;
            var memoryStatus = memoryPercent >= 50 ? HealthStatus.Critical : memoryPercent >= 25 ? HealthStatus.Warning : HealthStatus.Ok;
            Status = (HealthStatus)Math.Max((int)cpuStatus, (int)memoryStatus);
        }

        public string Name { get; }

        public double CpuPercent { get; }

        public string CpuText => Format.Percent(CpuPercent, 1);

        public long MemoryBytes { get; }

        public string MemoryText => Format.Bytes(MemoryBytes);

        public HealthStatus Status { get; }
    }

    /// <summary>Snapshot dos processos que mais consomem, por CPU e por memória.</summary>
    public sealed class ProcessSnapshot
    {
        public ProcessSnapshot(IReadOnlyList<ProcessUsageItem> byCpu, IReadOnlyList<ProcessUsageItem> byMemory)
        {
            ByCpu = byCpu;
            ByMemory = byMemory;
        }

        public IReadOnlyList<ProcessUsageItem> ByCpu { get; }

        public IReadOnlyList<ProcessUsageItem> ByMemory { get; }
    }

    /// <summary>Uso de uma unidade fixa (ex.: C:).</summary>
    public sealed class DiskUsageItem
    {
        private DiskUsageItem(string name, string label, string title, long totalBytes, long freeBytes)
        {
            Name = name;
            Label = label;
            Title = title;
            TotalBytes = totalBytes;
            FreeBytes = freeBytes;
            UsedBytes = Math.Max(0, totalBytes - freeBytes);
            UsedPercent = totalBytes > 0 ? Math.Max(0, Math.Min(100, UsedBytes * 100.0 / totalBytes)) : 0;
            UsedText = Format.Percent(UsedPercent) + " usado";
            FreeText = Format.Bytes(FreeBytes) + " livres";
            TotalText = Format.Bytes(TotalBytes);
            Status = UsedPercent >= 90 ? HealthStatus.Critical : UsedPercent >= 80 ? HealthStatus.Warning : HealthStatus.Ok;
        }

        public static DiskUsageItem Create(string driveName, string volumeLabel, long totalBytes, long freeBytes)
        {
            var name = string.IsNullOrWhiteSpace(driveName) ? driveName : driveName.TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(name)) name = driveName;

            var hasLabel = !string.IsNullOrWhiteSpace(volumeLabel);
            var label = "Disco " + name + (hasLabel ? " (" + volumeLabel.Trim() + ")" : string.Empty);
            var title = hasLabel ? name + " — " + volumeLabel.Trim() : name;
            return new DiskUsageItem(name, label, title, Math.Max(0, totalBytes), Math.Max(0, freeBytes));
        }

        public string Name { get; }

        /// <summary>Nome longo (usado em ToolTip).</summary>
        public string Label { get; }

        /// <summary>Texto curto exibido na lista.</summary>
        public string Title { get; }

        public long TotalBytes { get; }

        public long FreeBytes { get; }

        public long UsedBytes { get; }

        public double UsedPercent { get; }

        public string UsedText { get; }

        public string FreeText { get; }

        public string TotalText { get; }

        public HealthStatus Status { get; }
    }

    /// <summary>
    /// Métricas complementares do painel: uso por núcleo, processos que mais consomem e
    /// espaço por unidade fixa. As leituras de processos são diferenciais entre chamadas.
    /// </summary>
    public sealed class DashboardMetrics
    {
        private const int SystemProcessorPerformanceInformation = 8;

        private readonly object _coreSync = new object();
        private long[] _prevCoreIdle;
        private long[] _prevCoreTotal;
        private bool _hasCoreSample;
        private double[] _lastCoreUsage = new double[0];

        private readonly Dictionary<string, TimeSpan> _prevProcessCpu =
            new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);

        private DateTime _prevProcessSample;

        /// <summary>
        /// Uso de cada processador lógico desde a chamada anterior (0 na primeira).
        /// Retorna apenas números: a criação/atualização dos itens da UI é feita por
        /// <see cref="SyncCores"/> na thread do dispatcher. Pode ser chamado em background.
        /// </summary>
        public IReadOnlyList<double> GetCpuCores()
        {
            lock (_coreSync)
            {
                var count = Math.Max(1, Environment.ProcessorCount);
                var size = Marshal.SizeOf(typeof(NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));
                if (size <= 0) return _lastCoreUsage;

                var buffer = Marshal.AllocHGlobal(size * count);
                try
                {
                    var status = NativeMethods.NtQuerySystemInformation(
                        SystemProcessorPerformanceInformation, buffer, size * count, out var returnLength);
                    if (status != 0) return _lastCoreUsage;

                    // O kernel informa quantos bytes escreveu: nunca ler além disso
                    var written = returnLength > 0 ? returnLength / size : count;
                    var entries = Math.Min(count, Math.Max(0, written));
                    if (entries == 0) return _lastCoreUsage;

                    if (_prevCoreIdle == null || _prevCoreIdle.Length != count)
                    {
                        _prevCoreIdle = new long[count];
                        _prevCoreTotal = new long[count];
                        _hasCoreSample = false;
                    }

                    var values = new double[count];
                    for (var i = 0; i < entries; i++)
                    {
                        var info = (NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION)Marshal.PtrToStructure(
                            IntPtr.Add(buffer, i * size),
                            typeof(NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));

                        // KernelTime já inclui o tempo ocioso
                        var total = info.KernelTime + info.UserTime;
                        values[i] = _hasCoreSample
                            ? UsagePercent(info.IdleTime - _prevCoreIdle[i], total - _prevCoreTotal[i])
                            : 0;

                        _prevCoreIdle[i] = info.IdleTime;
                        _prevCoreTotal[i] = total;
                    }

                    _hasCoreSample = true;
                    _lastCoreUsage = values;
                    return values;
                }
                catch (Exception ex)
                {
                    Log.Warn("Falha ao ler o uso por núcleo: " + ex.Message);
                    return _lastCoreUsage;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        /// <summary>
        /// Copia a amostra para a coleção estável da UI, atualizando os itens existentes
        /// por índice. Só recria os itens quando a quantidade de processadores lógicos muda.
        /// Deve ser chamado na thread do dispatcher. Retorna true quando algo mudou.
        /// </summary>
        public static bool SyncCores(IList<CpuCoreUsage> target, IReadOnlyList<double> usage)
        {
            if (target == null || usage == null || usage.Count == 0) return false;

            var changed = false;
            if (target.Count != usage.Count)
            {
                target.Clear();
                for (var i = 0; i < usage.Count; i++) target.Add(new CpuCoreUsage(i));
                changed = true;
            }

            for (var i = 0; i < usage.Count && i < target.Count; i++)
            {
                var before = target[i].Usage;
                target[i].Update(usage[i]);
                if (Math.Abs(before - target[i].Usage) > 0.001) changed = true;
            }

            return changed;
        }

        /// <summary>Top processos por CPU e por memória (agrupados por nome).</summary>
        public ProcessSnapshot GetTopProcesses(int count = 5)
        {
            count = Math.Max(1, count);
            var now = DateTime.UtcNow;
            var seconds = _prevProcessSample == default ? 0 : (now - _prevProcessSample).TotalSeconds;
            var cores = Math.Max(1, Environment.ProcessorCount);

            var totals = new Dictionary<string, ProcessTotal>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var name = process.ProcessName;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    TimeSpan cpu;
                    try
                    {
                        cpu = process.TotalProcessorTime;
                    }
                    catch
                    {
                        cpu = TimeSpan.Zero;
                    }

                    long memory;
                    try
                    {
                        memory = process.WorkingSet64;
                    }
                    catch
                    {
                        memory = 0;
                    }

                    if (!totals.TryGetValue(name, out var current)) current = default;
                    totals[name] = new ProcessTotal(current.Cpu + cpu, current.Memory + Math.Max(0, memory));
                }
                catch
                {
                    // Processos protegidos/serviços encerrando no meio da leitura são ignorados
                }
                finally
                {
                    process.Dispose();
                }
            }

            var memoryTotal = SystemMetrics.GetMemory().Total;
            var items = new List<ProcessUsageItem>(totals.Count);
            foreach (var pair in totals)
            {
                var cpuPercent = 0.0;
                if (seconds > 0 && _prevProcessCpu.TryGetValue(pair.Key, out var previous))
                    cpuPercent = ProcessPercent(pair.Value.Cpu - previous, seconds, cores);

                items.Add(new ProcessUsageItem(pair.Key, cpuPercent, pair.Value.Memory, memoryTotal));
            }

            _prevProcessCpu.Clear();
            foreach (var pair in totals) _prevProcessCpu[pair.Key] = pair.Value.Cpu;
            _prevProcessSample = now;

            var byCpu = items
                .OrderByDescending(i => i.CpuPercent)
                .ThenByDescending(i => i.MemoryBytes)
                .Take(count)
                .ToList();

            var byMemory = items
                .OrderByDescending(i => i.MemoryBytes)
                .ThenByDescending(i => i.CpuPercent)
                .Take(count)
                .ToList();

            return new ProcessSnapshot(byCpu, byMemory);
        }

        /// <summary>Unidades fixas prontas (HDs desligados são ignorados para não acordá-los).</summary>
        public IReadOnlyList<DiskUsageItem> GetDisks()
        {
            var list = new List<DiskUsageItem>();
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch (Exception ex)
            {
                // Sem a enumeração não há como montar a lista: devolve snapshot vazio
                Log.Warn("Falha ao enumerar as unidades: " + ex.Message);
                return list;
            }

            foreach (var drive in drives)
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    list.Add(DiskUsageItem.Create(drive.Name, drive.VolumeLabel, drive.TotalSize, drive.TotalFreeSpace));
                }
                catch
                {
                    // Unidade removida/indisponível no meio da enumeração: mantém as demais
                }
            }

            return list.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Unidade onde o Windows está instalado, ou null quando não está pronta/legível.
        /// Lê o disco fora da thread da UI e devolve um snapshot pronto para exibição.
        /// </summary>
        public DiskUsageItem GetSystemDrive()
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                if (!drive.IsReady || drive.TotalSize <= 0) return null;
                return DiskUsageItem.Create(drive.Name, drive.VolumeLabel, drive.TotalSize, drive.TotalFreeSpace);
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao ler o disco do sistema: " + ex.Message);
                return null;
            }
        }

        /// <summary>Percentual de uso a partir dos tempos ociosos/totais (em 100 ns).</summary>
        public static double UsagePercent(double idleDelta, double totalDelta)
        {
            if (totalDelta <= 0) return 0;
            return Math.Max(0, Math.Min(100, (1.0 - idleDelta / totalDelta) * 100.0));
        }

        /// <summary>
        /// Percentual de CPU de um processo: tempo de CPU no intervalo dividido pelo
        /// tempo de parede × núcleos (assim 1 núcleo saturado em 8 = 12,5%).
        /// </summary>
        public static double ProcessPercent(TimeSpan cpuDelta, double wallSeconds, int cores)
        {
            if (wallSeconds <= 0 || cores <= 0 || cpuDelta <= TimeSpan.Zero) return 0;
            var percent = cpuDelta.TotalSeconds / (wallSeconds * cores) * 100.0;
            return Math.Max(0, Math.Min(100, percent));
        }

        private struct ProcessTotal
        {
            public ProcessTotal(TimeSpan cpu, long memory)
            {
                Cpu = cpu;
                Memory = memory;
            }

            public TimeSpan Cpu { get; }

            public long Memory { get; }
        }
    }
}
