using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using OtimizaPC.Core;
using OtimizaPC.Repair.Console;

namespace OtimizaPC.Repair.Tools
{
    /// <summary>
    /// Redefine os componentes do Windows Update: para os serviços, renomeia (não apaga) as pastas de cache
    /// SoftwareDistribution e catroot2, e religa os serviços. O Windows recria as pastas sozinho.
    /// </summary>
    public sealed class WindowsUpdateResetTool : RepairTool
    {
        private static readonly string[] Services = { "wuauserv", "bits", "cryptsvc", "msiserver" };
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

        public WindowsUpdateResetTool()
            : base("wu-reset", "Redefinir o Windows Update",
                "Resolve atualizações travadas ou que falham sempre. As pastas antigas são renomeadas com a data, não apagadas.",
                RepairGroups.Windows)
        {
            Duration = "1 a 3 minutos";
            NeedsConfirmation = true;
        }

        public override Task<RepairResult> RunAsync(Action<OutputLine> output, CancellationToken cancellationToken) =>
            Task.Run(() => Run(output, cancellationToken), cancellationToken);

        private RepairResult Run(Action<OutputLine> output, CancellationToken cancellationToken)
        {
            var stopped = new List<string>();
            var errors = 0;
            var suffix = ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

            try
            {
                foreach (var name in Services)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (var service = new ServiceController(name))
                    {
                        try
                        {
                            if (service.Status == ServiceControllerStatus.Stopped)
                            {
                                output(new OutputLine($"Serviço {name} já está parado.", false));
                                continue;
                            }
                            output(new OutputLine($"Parando o serviço {name}…", false));
                            service.Stop();
                            service.WaitForStatus(ServiceControllerStatus.Stopped, Timeout);
                            stopped.Add(name);
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            output(new OutputLine($"Não foi possível parar {name}: {ex.Message}", false, true));
                        }
                    }
                }

                var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                errors += Rename(Path.Combine(windows, "SoftwareDistribution"), suffix, output);
                errors += Rename(Path.Combine(Environment.SystemDirectory, "catroot2"), suffix, output);
            }
            finally
            {
                // Religa o que estava rodando, mesmo em caso de erro
                foreach (var name in stopped)
                {
                    using (var service = new ServiceController(name))
                    {
                        try
                        {
                            output(new OutputLine($"Iniciando o serviço {name}…", false));
                            service.Start();
                            service.WaitForStatus(ServiceControllerStatus.Running, Timeout);
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            output(new OutputLine($"Não foi possível iniciar {name}: {ex.Message}", false, true));
                        }
                    }
                }
            }

            var summary = errors == 0
                ? "Windows Update redefinido. Abra o Windows Update e procure atualizações de novo."
                : $"Concluído com {errors} erro(s). Veja a saída acima.";
            History.Record("Reparo", $"{Name}: {summary}");
            return new RepairResult { Success = errors == 0, Summary = summary };
        }

        private static int Rename(string folder, string suffix, Action<OutputLine> output)
        {
            if (!Directory.Exists(folder))
            {
                output(new OutputLine($"{folder} não existe (será criada pelo Windows).", false));
                return 0;
            }
            try
            {
                Directory.Move(folder, folder + suffix);
                output(new OutputLine($"Renomeada: {folder} → {Path.GetFileName(folder + suffix)}", false));
                return 0;
            }
            catch (Exception ex)
            {
                output(new OutputLine($"Não foi possível renomear {folder}: {ex.Message}", false, true));
                return 1;
            }
        }
    }
}
