using System;
using System.Diagnostics;
using OtimizaPC.Cleaning;
using Xunit;

namespace OtimizaPC.Tests
{
    /// <summary>Testes do encerramento limitado usado pelo schtasks (não alteram tarefas reais).</summary>
    public class ScheduledCleanupTests
    {
        private static Process Start(string arguments) =>
            Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });

        [Fact]
        public void Sucesso_RetornaCodigoDeSaida()
        {
            using (var process = Start("/c exit 0"))
                Assert.Equal(0, ScheduledCleanup.WaitForExitOrThrow(process, 10_000));
        }

        [Fact]
        public void Erro_RetornaCodigoSemTimeout()
        {
            using (var process = Start("/c exit 3"))
                Assert.Equal(3, ScheduledCleanup.WaitForExitOrThrow(process, 10_000));
        }

        [Fact]
        public void Timeout_EncerraProcessoEInformaErroAcionavel()
        {
            using (var process = Start("/c ping -n 10 127.0.0.1 > nul"))
            {
                var error = Assert.Throws<TimeoutException>(() => ScheduledCleanup.WaitForExitOrThrow(process, 300));

                Assert.True(process.HasExited, "o processo deve ser encerrado no timeout");
                Assert.Contains("Agendador de Tarefas", error.Message);
                Assert.Contains("encerrado", error.Message);
                Assert.Contains("administrador", error.Message);
            }
        }

        [Fact]
        public void ArgumentosInvalidos_SaoRejeitados()
        {
            Assert.Throws<ArgumentNullException>(() => ScheduledCleanup.WaitForExitOrThrow(null, 1000));

            using (var process = Start("/c exit 0"))
                Assert.Throws<ArgumentOutOfRangeException>(() => ScheduledCleanup.WaitForExitOrThrow(process, 0));
        }
    }
}
