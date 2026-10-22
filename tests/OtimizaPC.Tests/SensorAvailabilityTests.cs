using OtimizaPC.Hardware.Sensors;
using Xunit;

namespace OtimizaPC.Tests
{
    public class SensorAvailabilityTests
    {
        [Fact]
        public void TemperaturaDisponivelNaoExibeAviso()
        {
            Assert.Null(SensorAvailability.Message(SensorServiceState.Ready, null, false, false, true));
        }

        [Fact]
        public void DriverAusenteOfereceInstalacaoMesmoSemOutrosSensores()
        {
            Assert.Contains("Instale o driver PawnIO", SensorAvailability.Message(SensorServiceState.Ready, null, true, false, false));
        }

        [Fact]
        public void DriverInstaladoSemTemperaturaNaoRecomendaReinstalar()
        {
            var message = SensorAvailability.Message(SensorServiceState.Ready, null, true, true, false);
            Assert.Contains("não forneceu temperatura", message);
            Assert.DoesNotContain("Instale o driver", message);
        }

        [Fact]
        public void SemAdministradorExplicaPermissaoAntesDoDriver()
        {
            Assert.Contains("como administrador", SensorAvailability.Message(SensorServiceState.Ready, null, false, false, false));
        }

        [Fact]
        public void InicializacaoEFalhaNaoSaoConfundidasComDriverAusente()
        {
            Assert.Equal("Carregando sensores…", SensorAvailability.Message(SensorServiceState.Starting, null, true, false, false));
            Assert.Contains("erro de teste", SensorAvailability.Message(SensorServiceState.Failed, "erro de teste", true, false, false));
        }
    }
}
