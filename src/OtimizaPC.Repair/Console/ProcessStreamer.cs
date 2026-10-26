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
        private static readonly TimeSpan CancelGrace = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(5);

        public static Task<int> RunAsync(string fileName, string arguments, Action<OutputLine> onLine, CancellationToken cancellationToken) =>
            RunAsync(fileName, arguments, onLine, cancellationToken, onStarted: null);

        internal static async Task<int> RunAsync(string fileName, string arguments, Action<OutputLine> onLine,
            CancellationToken cancellationToken, Action<int> onStarted)
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
                onStarted?.Invoke(process.Id);

                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task termination = null;

                using (cancellationToken.Register(() =>
                {
                    // Encerrar a árvore pode levar alguns segundos: não bloqueia quem pediu o cancelamento (a UI)
                    termination = Task.Run(() => KillTree(process));
                    cancelled.TrySetResult(true);
                }))
                {
                    var pumps = Task.WhenAll(
                        PumpAsync(process.StandardOutput.BaseStream, onLine, isError: false),
                        PumpAsync(process.StandardError.BaseStream, onLine, isError: true));

                    try
                    {
                        var first = await Task.WhenAny(pumps, cancelled.Task).ConfigureAwait(false);

                        if (first == pumps)
                        {
                            // Saída encerrada: propaga falha de leitura, se houver, e confirma a saída do processo
                            await pumps.ConfigureAwait(false);
                            if (!process.WaitForExit((int)ExitGrace.TotalMilliseconds))
                            {
                                KillTree(process);
                                if (!process.WaitForExit((int)ExitGrace.TotalMilliseconds))
                                    throw new InvalidOperationException("O processo não encerrou depois de fechar a saída.");
                            }

                            Observe(termination);
                            return process.ExitCode;
                        }

                        // Cancelado: dá um tempo curto para drenar as últimas linhas e para o taskkill concluir.
                        // Se um processo neto sobreviver e segurar a saída, não espera indefinidamente.
                        var drain = termination == null ? pumps : Task.WhenAll(pumps, termination);
                        await Task.WhenAny(drain, Task.Delay(CancelGrace)).ConfigureAwait(false);

                        // Mesmo quando os pumps não terminam a tempo, as falhas deles precisam ser observadas
                        Observe(pumps);
                        Observe(termination);

                        if (!process.WaitForExit((int)CancelGrace.TotalMilliseconds)) KillTree(process);
                        cancellationToken.ThrowIfCancellationRequested();
                        return process.ExitCode; // inalcançável quando o token está cancelado
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Falha de leitura ou do consumidor das linhas: não deixa o processo órfão
                        KillTree(process);
                        throw;
                    }
                }
            }
        }

        private static async Task PumpAsync(Stream stream, Action<OutputLine> onLine, bool isError)
        {
            var buffer = new byte[4096];
            Decoder unicode = null;
            var decided = false;
            var chars = new char[8192];

            // Em modo byte, cada linha é decodificada sozinha (ver OutputDecoding)
            var splitter = new LineSplitter((text, progress) =>
            {
                var decoded = unicode != null ? text : OutputDecoding.DecodeLine(OutputDecoding.Latin1.GetBytes(text));
                int percent;
                onLine(new OutputLine(decoded, progress, isError,
                    ProgressParser.TryParse(decoded, out percent) ? (int?)percent : null));
            });

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

        /// <summary>Marca uma tarefa abandonada como observada (evita UnobservedTaskException).</summary>
        private static void Observe(Task task)
        {
            if (task == null) return;
            task.ContinueWith(t => { var ignored = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
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
