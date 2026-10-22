using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using OtimizaPC.Core;

namespace OtimizaPC.Cleaning.Cleaners
{
    /// <summary>
    /// Limpeza de componentes do Windows (WinSxS) via DISM /StartComponentCleanup.
    /// O tamanho só é conhecido depois, pela diferença de espaço livre na unidade do sistema.
    /// </summary>
    public sealed class ComponentStoreCleaner : Cleaner
    {
        public ComponentStoreCleaner()
            : base("component-store", "Componentes antigos do Windows (DISM)",
                "Remove versões substituídas de componentes após atualizações. Pode levar vários minutos.",
                CleanupGroups.Windows, CleanupRisk.Safe, requiresAdmin: true, defaultSelected: false)
        {
        }

        // DISM /StartComponentCleanup existe a partir do Windows 8 / Server 2012
        public override bool IsAvailable => Environment.OSVersion.Version >= new Version(6, 2);

        public override CleanupScan Scan(CancellationToken cancellationToken) =>
            new CleanupScan { SizeKnown = false, Note = "Tamanho conhecido só após executar" };

        public override CleanResult Clean(CleanupScan scan, CancellationToken cancellationToken)
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
            var freeBefore = drive.TotalFreeSpace;

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "dism.exe"),
                Arguments = "/Online /Cleanup-Image /StartComponentCleanup /NoRestart",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            // Interromper o DISM no meio pode deixar o repositório inconsistente: não há cancelamento aqui
            using (var process = Process.Start(startInfo))
            {
                process.OutputDataReceived += (_, __) => { };
                process.ErrorDataReceived += (_, __) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                drive = new DriveInfo(drive.Name);
                var freed = Math.Max(0, drive.TotalFreeSpace - freeBefore);
                if (process.ExitCode != 0)
                {
                    Log.Warn("DISM StartComponentCleanup terminou com código " + process.ExitCode);
                    return new CleanResult { FreedBytes = freed, Note = $"O DISM terminou com código {process.ExitCode}" };
                }
                return new CleanResult { FreedBytes = freed, Deleted = 1 };
            }
        }
    }
}
