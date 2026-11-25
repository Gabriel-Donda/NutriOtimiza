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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AlteracaoDoAgendamento_ExigeCodigoZero(bool enable)
        {
            ScheduledCleanup.EnsureSchtasksSucceeded(0, enable);

            var error = Assert.Throws<InvalidOperationException>(
                () => ScheduledCleanup.EnsureSchtasksSucceeded(1, enable));
            Assert.Contains(enable ? "criar" : "remover", error.Message);
            Assert.Contains("código 1", error.Message);
            if (enable)
                Assert.Contains("Execute o NutriOtimiza como administrador e verifique se o serviço 'Agendador de Tarefas' está em execução.", error.Message);
        }

        [Theory]
        [InlineData(0, true, true)]
        [InlineData(0, false, true)]
        [InlineData(1, false, true)]
        [InlineData(1, true, false)]
        public void Exclusao_AceitaTarefaAusente(int exitCode, bool taskExists, bool success)
        {
            Assert.Equal(success, ScheduledCleanup.IsDeleteSuccessful(exitCode, taskExists));
        }

        [Theory]
        [InlineData(unchecked((int)0x80070002), true)]
        [InlineData(unchecked((int)0x80070003), true)]
        [InlineData(unchecked((int)0x80070005), false)]
        [InlineData(unchecked((int)0x800706BA), false)]
        public void Consulta_DistingueTarefaAusenteDeFalha(int hresult, bool missing)
        {
            Assert.Equal(missing, ScheduledCleanup.IsMissingTask(hresult));
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
