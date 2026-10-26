using System;
using System.ComponentModel;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using OtimizaPC.Core;
using OtimizaPC.Repair.Console;
using OtimizaPC.Repair.Tools;

namespace OtimizaPC.Repair.Fixes
{
    internal sealed class ServiceSnapshot
    {
        public string Name { get; private set; }
        public string DisplayName { get; private set; }
        public string ImagePath { get; private set; }
        public bool IsThirdParty { get; private set; }

        public static ServiceSnapshot Read(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            using (var service = new ServiceController(name))
            {
                // Ler o status também confirma que o nome existe antes de consultar a identidade.
                var ignored = service.Status;
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
                {
                    if (key == null) return null;
                    var image = Convert.ToString(key.GetValue("ImagePath"));
                    var display = Convert.ToString(key.GetValue("DisplayName")) ?? service.DisplayName;
                    var expanded = Environment.ExpandEnvironmentVariables(image ?? string.Empty).Trim();
                    if (expanded.StartsWith("\"", StringComparison.Ordinal))
                    {
                        var end = expanded.IndexOf('"', 1);
                        expanded = end > 1 ? expanded.Substring(1, end - 1) : expanded.Trim('"');
                    }
                    else
                    {
                        var separator = expanded.IndexOf(' ');
                        if (separator > 0) expanded = expanded.Substring(0, separator);
                    }
                    var windows = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\');
                    var normalized = string.IsNullOrWhiteSpace(expanded) ? string.Empty : Path.GetFullPath(expanded);
                    return new ServiceSnapshot
                    {
                        Name = name,
                        DisplayName = display,
                        ImagePath = image,
                        IsThirdParty = !string.IsNullOrWhiteSpace(normalized)
                            && !normalized.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase),
                    };
                }
            }
        }
    }

    internal sealed class ServiceStartTool : RepairTool
    {
        private readonly string _serviceName;
        private readonly string _expectedDisplayName;
        private readonly string _expectedImagePath;

        public ServiceStartTool(string serviceName, string expectedDisplayName, string expectedImagePath = null)
            : base("service-start-" + serviceName.ToLowerInvariant(), "Iniciar o serviço " + serviceName,
                "Inicia apenas o serviço identificado pela mensagem estruturada do SCM.", RepairGroups.Windows)
        {
            _serviceName = serviceName;
            _expectedDisplayName = expectedDisplayName;
            _expectedImagePath = expectedImagePath;
            NeedsConfirmation = true;
        }

        public override Task<RepairResult> RunAsync(Action<OutputLine> output, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = ServiceSnapshot.Read(_serviceName);
            if (current == null || !string.Equals(current.DisplayName, _expectedDisplayName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(current.ImagePath, _expectedImagePath, StringComparison.Ordinal))
                throw new InvalidOperationException("A identidade do serviço mudou; o reparo foi cancelado.");
            using (var service = new ServiceController(_serviceName))
            {
                if (service.Status == ServiceControllerStatus.Running)
                    return Task.FromResult(new RepairResult { Success = true, Summary = "O serviço já está em execução." });
                var beforeStart = ServiceSnapshot.Read(_serviceName);
                if (beforeStart == null
                    || !string.Equals(beforeStart.DisplayName, _expectedDisplayName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(beforeStart.ImagePath, _expectedImagePath, StringComparison.Ordinal))
                    throw new InvalidOperationException("A identidade do serviço mudou antes do início; o reparo foi cancelado.");
                output(new OutputLine("Iniciando o serviço " + _serviceName + "…", false));
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }
            var result = new RepairResult { Success = true, Summary = "Serviço iniciado com a identidade validada." };
            History.Record("Reparo", Name + ": " + result.Summary);
            return Task.FromResult(result);
        }
    }

    internal static class SpoolerStateMachine
    {
        public static void Execute(bool wasRunning, Action stop, Action start, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!wasRunning) return;
            try
            {
                stop();
            }
            finally
            {
                // Depois de parar, não há novo ponto de cancelamento: restaure sempre o estado inicial.
                try { start(); }
                catch (Exception) { throw; }
            }
        }
    }

    internal sealed class SpoolerRestartTool : RepairTool
    {
        public SpoolerRestartTool()
            : base("print-spooler-restart", "Reiniciar a fila de impressão",
                "Reinicia o Spooler e sempre restaura o estado inicial do serviço.", RepairGroups.Windows)
        {
            NeedsConfirmation = true;
        }

        public override Task<RepairResult> RunAsync(Action<OutputLine> output, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var service = new ServiceController("Spooler"))
            {
                var wasRunning = service.Status == ServiceControllerStatus.Running;
                if (!wasRunning)
                    return Task.FromResult(new RepairResult { Success = true, Summary = "O Spooler já estava parado; nenhum estado foi alterado." });
                var errors = 0;
                try
                {
                    SpoolerStateMachine.Execute(wasRunning,
                        () => { output(new OutputLine("Parando o Spooler…", false)); service.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); },
                        () => { output(new OutputLine("Restaurando o Spooler…", false)); service.Start(); service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)); },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    errors++;
                    output(new OutputLine("Falha ao reiniciar o Spooler: " + ex.Message, false, true));
                }
                var result = new RepairResult
                {
                    Success = errors == 0,
                    Summary = errors == 0 ? "Spooler reiniciado e restaurado ao estado inicial." : "O Spooler terminou com erro; veja a saída acima.",
                };
                History.Record("Reparo", Name + ": " + result.Summary);
                return Task.FromResult(result);
            }
        }
    }
}
