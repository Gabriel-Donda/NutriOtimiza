using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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

        public static bool IsEnabled() => QueryEnabled() == true;

        // Null indica que a tarefa ou sua pasta está ausente.
        private static bool? QueryEnabled(bool checkEnabled = true)
        {
            var service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true));
            object folder = null;
            object task = null;
            try
            {
                InvokeSchedulerMember(service, "Connect", null, null, null, null);
                try
                {
                    var separator = TaskName.LastIndexOf('\\');
                    folder = InvokeSchedulerMember(service, "GetFolder", @"\" + TaskName.Substring(0, separator));
                    task = InvokeSchedulerMember(folder, "GetTask", TaskName.Substring(separator + 1));
                }
                catch (TargetInvocationException ex) when (IsMissingTask(ex.InnerException.HResult))
                {
                    return null;
                }
                catch (Exception ex) when (IsMissingTask(ex.HResult))
                {
                    return null;
                }

                return !checkEnabled || (bool)InvokeSchedulerMember(task, "Enabled");
            }
            finally
            {
                if (task != null) Marshal.ReleaseComObject(task);
                if (folder != null) Marshal.ReleaseComObject(folder);
                Marshal.ReleaseComObject(service);
            }
        }

        private static object InvokeSchedulerMember(object target, string member, params object[] arguments)
        {
            return target.GetType().InvokeMember(member, BindingFlags.InvokeMethod | BindingFlags.GetProperty,
                null, target, arguments);
        }

        internal static bool IsMissingTask(int hresult)
        {
            // HRESULT de arquivo ou caminho ausente, incluindo a pasta da tarefa.
            return hresult == unchecked((int)0x80070002) || hresult == unchecked((int)0x80070003);
        }

        /// <summary>Cria ou substitui a tarefa semanal de domingo ao meio-dia.</summary>
        public static void Enable(string executablePath)
        {
            var xmlPath = Path.Combine(Path.GetTempPath(), AppInfo.Name + "-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                // O schtasks exige o XML de tarefa em UTF-16.
                File.WriteAllText(xmlPath, BuildTaskXml(executablePath, DateTime.Now), System.Text.Encoding.Unicode);
                var code = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
                EnsureSchtasksSucceeded(code, enable: true);
                History.Record("Limpeza automática", "Agendamento semanal ativado");
            }
            finally
            {
                try
                {
                    File.Delete(xmlPath);
                }
                catch
                {
                    // Não deixa a falha de limpeza do temporário esconder o erro real do agendamento.
                }
            }
        }

        public static void Disable()
        {
            var code = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            if (code != 0 && !IsDeleteSuccessful(code, QueryEnabled(checkEnabled: false).HasValue))
                EnsureSchtasksSucceeded(code, enable: false);
            History.Record("Limpeza automática", "Agendamento desativado");
        }

        internal static bool IsDeleteSuccessful(int exitCode, bool taskExists) =>
            exitCode == 0 || !taskExists;

        internal static void EnsureSchtasksSucceeded(int exitCode, bool enable)
        {
            if (exitCode == 0) return;

            var operation = enable ? "criar" : "remover";
            var message = $"Não foi possível {operation} a tarefa de limpeza (código {exitCode}).";
            if (enable)
                message += " Execute o NutriOtimiza como administrador e verifique se o serviço 'Agendador de Tarefas' está em execução.";

            throw new InvalidOperationException(message);
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

            throw new TimeoutException(
                $"O Agendador de Tarefas não respondeu em {timeoutMs / 1000.0:0.#} s e foi encerrado. " +
                "Verifique se o serviço 'Agendador de Tarefas' está em execução e tente novamente como administrador.");
        }
    }
}
