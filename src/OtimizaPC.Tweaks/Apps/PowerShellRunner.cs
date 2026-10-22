using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace OtimizaPC.Tweaks.Apps
{
    internal sealed class PowerShellResult
    {
        public int ExitCode { get; set; }

        public string Output { get; set; }

        public string Error { get; set; }

        public bool Success => ExitCode == 0 && string.IsNullOrWhiteSpace(Error);
    }

    /// <summary>
    /// Executa o Windows PowerShell (4.0 no 8.1/2012 R2, 5.1 no 10/11) com o script codificado em Base64,
    /// evitando problemas de aspas e acentos na linha de comando.
    /// </summary>
    internal static class PowerShellRunner
    {
        private const string Prelude = "$ProgressPreference='SilentlyContinue'; $ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; ";

        public static PowerShellResult Run(string script, int timeoutMs = 120_000)
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Prelude + script));
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
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
                    throw new TimeoutException("O PowerShell não respondeu a tempo.");
                }
                return new PowerShellResult { ExitCode = process.ExitCode, Output = output.Result, Error = CleanError(error.Result) };
            }
        }

        /// <summary>Single quotes para usar valores dentro de scripts com segurança.</summary>
        public static string Quote(string value) => "'" + (value ?? string.Empty).Replace("'", "''") + "'";

        // O PowerShell escreve erros em CLIXML quando a saída é redirecionada; fica só a mensagem
        private static string CleanError(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (!raw.Contains("<Objs")) return raw.Trim();
            var sb = new StringBuilder();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(raw, "<S S=\"Error\">(.*?)</S>"))
                sb.Append(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Replace("_x000D__x000A_", " "));
            var text = sb.ToString().Trim();
            return text.Length == 0 ? null : text;
        }
    }
}
