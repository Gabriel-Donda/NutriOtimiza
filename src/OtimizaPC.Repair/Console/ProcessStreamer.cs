using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OtimizaPC.Repair.Console
{
    /// <summary>Executa um processo e entrega cada linha de saída em tempo real, com a codificação detectada.</summary>
    public static class ProcessStreamer
    {
        public static async Task<int> RunAsync(string fileName, string arguments, Action<OutputLine> onLine, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();
                var cancelled = new TaskCompletionSource<bool>();
                using (cancellationToken.Register(() =>
                {
                    KillTree(process);
                    cancelled.TrySetResult(true);
                }))
                {
                    var pumps = Task.WhenAll(
                        PumpAsync(process.StandardOutput.BaseStream, onLine, isError: false),
                        PumpAsync(process.StandardError.BaseStream, onLine, isError: true));

                    // Ao cancelar, um processo neto que sobreviva pode segurar a saída aberta: não espera por ele
                    var first = await Task.WhenAny(pumps, cancelled.Task).ConfigureAwait(false);
                    if (first != pumps) await Task.WhenAny(pumps, Task.Delay(CancelGrace)).ConfigureAwait(false);

                    cancellationToken.ThrowIfCancellationRequested();
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
        }

        private static readonly TimeSpan CancelGrace = TimeSpan.FromSeconds(3);

        private static async Task PumpAsync(Stream stream, Action<OutputLine> onLine, bool isError)
        {
            var buffer = new byte[4096];
            Decoder unicode = null;
            var decided = false;
            var chars = new char[8192];

            // Em modo byte, cada linha é decodificada sozinha (ver OutputDecoding)
            var splitter = new LineSplitter((text, progress) =>
                onLine(new OutputLine(unicode != null ? text : OutputDecoding.DecodeLine(OutputDecoding.Latin1.GetBytes(text)), progress, isError)));

            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                if (!decided)
                {
                    decided = true;
                    if (OutputDecoding.LooksLikeUtf16(buffer, read)) unicode = Encoding.Unicode.GetDecoder();
                }

                var count = unicode != null
                    ? unicode.GetChars(buffer, 0, read, chars, 0)
                    : OutputDecoding.Latin1.GetChars(buffer, 0, read, chars, 0);
                splitter.Feed(chars, count);
            }
            splitter.Flush();
        }

        /// <summary>
        /// Encerra o processo e todos os filhos (ex.: cmd → ping, dism → dismhost). Process.Kill do .NET Framework
        /// mata só o processo principal, e os filhos continuariam rodando com a saída aberta.
        /// </summary>
        private static void KillTree(Process process)
        {
            try
            {
                if (process.HasExited) return;
                var taskkill = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), $"/PID {process.Id} /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var killer = Process.Start(taskkill))
                    killer?.WaitForExit(5000);
                if (!process.HasExited) process.Kill();
            }
            catch
            {
                // Processo já terminou
            }
        }
    }
}
