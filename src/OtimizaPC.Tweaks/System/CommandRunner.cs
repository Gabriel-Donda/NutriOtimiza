using System;
using System.Diagnostics;
using System.IO;
using OtimizaPC.Core;

namespace OtimizaPC.Tweaks.SystemTools
{
    internal sealed class CommandResult
    {
        public int ExitCode { get; set; }

        public string Output { get; set; }
    }

    /// <summary>Executa ferramentas do System32 (powercfg etc.) e captura a saída.</summary>
    internal static class CommandRunner
    {
        public static CommandResult Run(string tool, string arguments, int timeoutMs = 60_000)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, tool),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var process = Process.Start(startInfo))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Já terminou
                    }
                    throw new TimeoutException($"{tool} não respondeu em {timeoutMs / 1000} s");
                }

                var result = new CommandResult { ExitCode = process.ExitCode, Output = output.Result + error.Result };
                if (result.ExitCode != 0)
                    Log.Warn($"{tool} {arguments} terminou com código {result.ExitCode}: {result.Output.Trim()}");
                return result;
            }
        }

        public static void RunChecked(string tool, string arguments)
        {
            var result = Run(tool, arguments);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"{tool} falhou (código {result.ExitCode}): {result.Output.Trim()}");
        }
    }
}
