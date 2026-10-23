using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OtimizaPC.Repair.Console;
using OtimizaPC.Repair.Diagnostics;
using OtimizaPC.Repair.Tools;
using Xunit;

namespace OtimizaPC.Tests
{
    public class RepairTests
    {
        // ---------- Codificação da saída (bytes reais capturados das ferramentas em pt-BR) ----------

        [Fact]
        public void Decodifica_DismEmOem850()
        {
            // "Permissões obrigatórias" como o DISM escreve (cp850)
            var bytes = Encoding.GetEncoding(850).GetBytes("Permissões elevadas obrigatórias");
            Assert.Equal("Permissões elevadas obrigatórias", OutputDecoding.DecodeLine(bytes));
        }

        [Fact]
        public void Decodifica_ChkdskEmAnsi1252()
        {
            var bytes = Encoding.GetEncoding(1252).GetBytes("Verifica um disco e exibe um relatório de status.");
            Assert.Equal("Verifica um disco e exibe um relatório de status.", OutputDecoding.DecodeLine(bytes));
        }

        [Fact]
        public void Decodifica_NetshEmUtf8()
        {
            var bytes = Encoding.UTF8.GetBytes("Os seguintes comandos estão disponíveis:");
            Assert.Equal("Os seguintes comandos estão disponíveis:", OutputDecoding.DecodeLine(bytes));
        }

        [Fact]
        public void Detecta_SfcEmUtf16()
        {
            var bytes = Encoding.Unicode.GetBytes("\r\r\nVocê precisa ser um administrador");
            Assert.True(OutputDecoding.LooksLikeUtf16(bytes, bytes.Length));
            var ascii = Encoding.ASCII.GetBytes("\r\nErro: 740\r\nPermissoes");
            Assert.False(OutputDecoding.LooksLikeUtf16(ascii, ascii.Length));
        }

        // ---------- Separação de linhas e progresso ----------

        private static List<(string Text, bool Progress)> Split(string input, int chunk = 3)
        {
            var lines = new List<(string, bool)>();
            var splitter = new LineSplitter((t, p) => lines.Add((t, p)));
            var chars = input.ToCharArray();
            // Em pedaços pequenos, para testar quebras divididas entre leituras
            for (var i = 0; i < chars.Length; i += chunk)
                splitter.Feed(chars.Skip(i).Take(chunk).ToArray(), Math.Min(chunk, chars.Length - i));
            splitter.Flush();
            return lines;
        }

        [Fact]
        public void Linhas_ProgressoComCarriageReturn()
        {
            var lines = Split("Inicio\r\n[== 10%]\r[==== 20%]\rFim\r\n");
            Assert.Equal(new[] { ("Inicio", false), ("[== 10%]", true), ("[==== 20%]", true), ("Fim", false) }, lines);
        }

        [Fact]
        public void Linhas_SfcComCrCrLf()
        {
            var lines = Split("Linha 1\r\r\nLinha 2\r\r\n", chunk: 1);
            Assert.Equal(new[] { ("Linha 1", false), ("Linha 2", false) }, lines);
        }

        [Fact]
        public void Linhas_UltimaSemQuebra()
        {
            Assert.Equal(new[] { ("a", false), ("b", false) }, Split("a\nb"));
        }

        [Fact]
        public async Task Processo_TransmiteLinhasECodigoDeSaida()
        {
            var lines = new List<OutputLine>();
            var code = await ProcessStreamer.RunAsync("cmd.exe", "/c echo primeira& echo segunda& exit /b 3",
                line => { lock (lines) lines.Add(line); }, CancellationToken.None);

            Assert.Equal(3, code);
            Assert.Contains(lines, l => l.Text.Trim() == "primeira");
            Assert.Contains(lines, l => l.Text.Trim() == "segunda");
        }

        [Fact]
        public async Task Processo_CancelamentoEncerraArvoreRapidamente()
        {
            // cmd → ping: o processo neto não pode segurar o cancelamento até o fim (30 s)
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using (var cts = new CancellationTokenSource())
            {
                var task = ProcessStreamer.RunAsync("cmd.exe", "/c ping -n 30 127.0.0.1", _ => cts.Cancel(), cts.Token);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            }
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Cancelamento levou {watch.Elapsed.TotalSeconds:N1} s");
        }

        [Fact]
        public async Task Processo_CanceladoNaoFicaAtivo()
        {
            var pid = 0;
            using (var cts = new CancellationTokenSource())
            {
                var task = ProcessStreamer.RunAsync("cmd.exe", "/c ping -n 30 127.0.0.1",
                    _ => cts.Cancel(), cts.Token, id => pid = id);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            }

            Assert.True(pid > 0, "o processo não chegou a iniciar");
            await WaitForExitAsync(pid);
        }

        [Fact]
        public async Task Processo_FalhaDoConsumidorEncerraProcesso()
        {
            var pid = 0;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessStreamer.RunAsync(
                "cmd.exe", "/c echo linha& ping -n 30 127.0.0.1",
                _ => throw new InvalidOperationException("falha simulada"),
                CancellationToken.None,
                id => pid = id));

            Assert.Equal("falha simulada", error.Message);
            Assert.True(pid > 0, "o processo não chegou a iniciar");
            await WaitForExitAsync(pid);
        }

        private static async Task WaitForExitAsync(int pid)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    using (var process = Process.GetProcessById(pid))
                        if (process.HasExited) return;
                }
                catch (ArgumentException)
                {
                    return; // não existe mais: encerrado
                }

                await Task.Delay(100);
            }

            Assert.True(false, $"o processo {pid} continua ativo depois de 5 s");
        }

        // ---------- Telas azuis ----------

        [Fact]
        public void Dump64_LeCodigoEParametros()
        {
            var bytes = new byte[0x60];
            Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(bytes, 0);
            BitConverter.GetBytes(0x133u).CopyTo(bytes, 0x38);
            BitConverter.GetBytes(1ul).CopyTo(bytes, 0x40);
            BitConverter.GetBytes(0x1E00ul).CopyTo(bytes, 0x48);

            Assert.True(DumpHeader.TryParse(bytes, out var header));
            Assert.True(header.Is64Bit);
            Assert.Equal(0x133ul, header.BugCheckCode);
            Assert.Equal(new ulong[] { 1, 0x1E00, 0, 0 }, header.Parameters);
        }

        [Fact]
        public void Dump32_LeCodigo()
        {
            var bytes = new byte[0x60];
            Encoding.ASCII.GetBytes("PAGEDUMP").CopyTo(bytes, 0);
            BitConverter.GetBytes(0xD1u).CopyTo(bytes, 0x28);
            BitConverter.GetBytes(0x10u).CopyTo(bytes, 0x2C);

            Assert.True(DumpHeader.TryParse(bytes, out var header));
            Assert.False(header.Is64Bit);
            Assert.Equal(0xD1ul, header.BugCheckCode);
            Assert.Equal(0x10ul, header.Parameters[0]);
        }

        [Fact]
        public void Dump_RejeitaArquivoInvalido()
        {
            Assert.False(DumpHeader.TryParse(new byte[0x60], out _));
        }

        [Theory]
        [InlineData("O computador foi reinicializado após uma verificação de erro. A verificação de erro foi: 0x0000009f (0x0000000000000003, 0xffffa58d1c6e2060, 0xfffff80562e77750, 0xffffa58d2b1d1a20). Um despejo foi salvo em: C:\\Windows\\Minidump\\101524-10234-01.dmp. ID do Relatório: abc.")]
        [InlineData("The computer has rebooted from a bugcheck.  The bugcheck was: 0x0000009f (0x0000000000000003, 0xffffa58d1c6e2060, 0xfffff80562e77750, 0xffffa58d2b1d1a20). A dump was saved in: C:\\Windows\\Minidump\\101524-10234-01.dmp. Report Id: abc.")]
        public void EventoBugCheck_ExtraiCodigoEDump(string message)
        {
            var parsed = CrashAnalyzer.ParseBugCheckMessage(message);
            Assert.Equal("0x0000009F", parsed.Code);
            Assert.StartsWith("0x0000000000000003", parsed.Parameters);
            Assert.Equal(@"C:\Windows\Minidump\101524-10234-01.dmp", parsed.DumpFile);
            Assert.Equal("DRIVER_POWER_STATE_FAILURE", BugCheckCodes.Describe(parsed.Code).Name);
        }

        [Theory]
        [InlineData("0x1000007E", "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED")]
        [InlineData("0x00000124", "WHEA_UNCORRECTABLE_ERROR")]
        [InlineData("0xC000021A", "STATUS_SYSTEM_PROCESS_TERMINATED")]
        public void CodigosDeParada_ReconheceVariantes(string code, string expected)
        {
            Assert.Equal(expected, BugCheckCodes.Describe(code).Name);
        }

        // ---------- Resumos e catálogo ----------

        [Fact]
        public void Resumo_Sfc()
        {
            Assert.Equal("Nenhum arquivo corrompido encontrado.",
                RepairCatalog.SummarizeSfc(new[] { "Windows Resource Protection did not find any integrity violations." }, true));
            Assert.Contains("DISM", RepairCatalog.SummarizeSfc(new[] { "Windows Resource Protection found corrupt files but was unable to fix some of them." }, true));
        }

        [Fact]
        public void Catalogo_FerramentasValidas()
        {
            var tools = RepairCatalog.Tools();
            Assert.Equal(tools.Count, tools.Select(t => t.Id).Distinct().Count());
            Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Duration)));
            Assert.NotEmpty(RepairCatalog.Shortcuts());
        }

        [Fact]
        public void LogDeEventos_LeSemAdministrador()
        {
            // Os logs Sistema e Aplicativo são legíveis por qualquer usuário
            var summary = EventLogAnalyzer.Analyze(1, CancellationToken.None);
            Assert.NotNull(summary);
        }
    }
}
