using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using OtimizaPC.Core;

namespace OtimizaPC.Cleaning
{
    /// <summary>
    /// Limpeza sem interface, usada pela tarefa agendada (NutriOtimiza.exe --limpeza-automatica).
    /// Só itens seguros e marcados como recomendados; caches de apps abertos são pulados.
    /// </summary>
    public static class AutoCleanup
    {
        public const string Argument = "--limpeza-automatica";

        public static long Run(CancellationToken cancellationToken)
        {
            var admin = AppInfo.IsAdministrator;
            var cleaners = CleanupCatalog.Create()
                .Where(c => c.Risk == CleanupRisk.Safe && c.DefaultSelected && (!c.RequiresAdmin || admin))
                .ToList();

            long freed = 0;
            foreach (var cleaner in cleaners)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (cleaner.GetBlockedReason() != null) continue;
                try
                {
                    var scan = cleaner.Scan(cancellationToken);
                    if (!scan.SizeKnown || scan.ItemCount == 0) continue;
                    freed += cleaner.Clean(scan, cancellationToken).FreedBytes;
                }
                catch (Exception ex)
                {
                    Log.Warn($"Limpeza automática: falha em '{cleaner.Id}': {ex.Message}");
                }
            }

            History.Record("Limpeza automática", $"{Format.Bytes(freed)} liberados ({cleaners.Count} itens verificados)");
            return freed;
        }
    }

    /// <summary>Tarefa semanal no Agendador de Tarefas do Windows que executa a limpeza automática.</summary>
    public static class ScheduledCleanup
    {
        public const string TaskName = @"NutriOtimiza\LimpezaAutomatica";
        private const int SchtasksTimeoutMs = 30_000;

        public static bool IsEnabled()
        {
            try
            {
                return RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Cria (ou substitui) a tarefa: domingo ao meio-dia, ou assim que o PC ligar se estiver desligado nesse horário.</summary>
        public static void Enable(string executablePath)
        {
            var xmlPath = Path.Combine(Path.GetTempPath(), AppInfo.Name + "-tarefa.xml");
            // O schtasks exige o XML de tarefa em UTF-16
            File.WriteAllText(xmlPath, BuildTaskXml(executablePath, DateTime.Now), System.Text.Encoding.Unicode);
            try
            {
                var code = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
                if (code != 0) throw new InvalidOperationException($"O Agendador de Tarefas recusou a criação (código {code}). É preciso executar como administrador.");
                History.Record("Limpeza automática", "Agendamento semanal ativado");
            }
            finally
            {
                File.Delete(xmlPath);
            }
        }

        public static void Disable()
        {
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            History.Record("Limpeza automática", "Agendamento desativado");
        }

        internal static string BuildTaskXml(string executablePath, DateTime now)
        {
            var start = now.Date.AddDays(((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7).AddHours(12);
            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Limpeza semanal de arquivos temporários e caches feita pelo NutriOtimiza.</Description>
  </RegistrationInfo>
  <Triggers>
    <CalendarTrigger>
      <StartBoundary>{start:yyyy-MM-ddTHH:mm:ss}</StartBoundary>
      <ScheduleByWeek>
        <DaysOfWeek><Sunday /></DaysOfWeek>
        <WeeksInterval>1</WeeksInterval>
      </ScheduleByWeek>
    </CalendarTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <StartWhenAvailable>true</StartWhenAvailable>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{SecurityElement.Escape(executablePath)}</Command>
      <Arguments>{AutoCleanup.Argument}</Arguments>
    </Exec>
  </Actions>
</Task>";
        }

        private static int RunSchtasks(string arguments)
        {
            var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                    throw new InvalidOperationException("Não foi possível iniciar o Agendador de Tarefas.");

                return WaitForExitOrThrow(process, SchtasksTimeoutMs);
            }
        }

        /// <summary>Espera o processo terminar ou o encerra quando excede o limite.</summary>
        internal static int WaitForExitOrThrow(Process process, int timeoutMs)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            if (process.WaitForExit(timeoutMs))
            {
                process.WaitForExit();
                return process.ExitCode;
            }

            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch
            {
                // O processo pode ter terminado entre o timeout e o Kill.
            }

            try
            {
                process.WaitForExit(5_000);
            }
            catch
            {
                // O timeout original continua sendo o erro relevante para o chamador.
            }

            throw new TimeoutException($"O Agendador de Tarefas não respondeu em {timeoutMs / 1000} s.");
        }
    }
}
