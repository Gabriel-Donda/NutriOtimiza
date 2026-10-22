using System;
using System.Collections.Generic;
using System.ServiceProcess;
using System.Threading;
using OtimizaPC.Cleaning.Files;
using OtimizaPC.Core;

namespace OtimizaPC.Cleaning.Cleaners
{
    /// <summary>
    /// Cache de downloads do Windows Update. Os serviços wuauserv e BITS travam esses arquivos,
    /// então são parados durante a limpeza e religados em seguida.
    /// </summary>
    public sealed class WindowsUpdateCleaner : FileCleaner
    {
        private static readonly string[] Services = { "wuauserv", "bits" };
        private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);

        public WindowsUpdateCleaner()
            : base("windows-update", "Cache do Windows Update",
                "Atualizações já instaladas que ficaram baixadas. O Windows baixa de novo se precisar.",
                CleanupGroups.Windows,
                new[] { new FileTarget(@"%WINDIR%\SoftwareDistribution\Download") },
                requiresAdmin: true)
        {
        }

        public override CleanResult Clean(CleanupScan scan, CancellationToken cancellationToken)
        {
            var stopped = new List<ServiceController>();
            try
            {
                foreach (var name in Services)
                {
                    var service = new ServiceController(name);
                    try
                    {
                        if (service.Status == ServiceControllerStatus.Stopped)
                        {
                            service.Dispose();
                            continue;
                        }
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
                        stopped.Add(service);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Não foi possível parar o serviço {name}: {ex.Message}");
                        service.Dispose();
                    }
                }

                return base.Clean(scan, cancellationToken);
            }
            finally
            {
                foreach (var service in stopped)
                {
                    try
                    {
                        service.Start();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Não foi possível religar o serviço {service.ServiceName}: {ex.Message}");
                    }
                    service.Dispose();
                }
            }
        }
    }
}
