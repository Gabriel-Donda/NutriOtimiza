using OtimizaPC.Core.Settings;
using Xunit;

namespace OtimizaPC.Tests
{
    public class AnimationPolicyTests
    {
        [Fact]
        public void PreferenciaDesligadaDesligaMesmoComSistemaLiberando()
        {
            var reason = AnimationPolicy.SystemBlockReason(clientAreaAnimation: true, remoteSession: false, weakHardware: false);

            Assert.Null(reason);
            Assert.False(AnimationPolicy.IsEnabled(userEnabled: false, systemBlockReason: reason));
        }

        [Fact]
        public void LiberadaQuandoUsuarioQuerESistemaPermite()
        {
            var reason = AnimationPolicy.SystemBlockReason(clientAreaAnimation: true, remoteSession: false, weakHardware: false);

            Assert.True(AnimationPolicy.IsEnabled(userEnabled: true, systemBlockReason: reason));
        }

        [Fact]
        public void EfeitosDesativadosNoWindowsBloqueiamMesmoComPreferenciaLigada()
        {
            var reason = AnimationPolicy.SystemBlockReason(clientAreaAnimation: false, remoteSession: false, weakHardware: false);

            Assert.NotNull(reason);
            Assert.False(AnimationPolicy.IsEnabled(userEnabled: true, systemBlockReason: reason));
        }

        [Fact]
        public void AcessoRemotoBloqueia()
        {
            var reason = AnimationPolicy.SystemBlockReason(clientAreaAnimation: true, remoteSession: true, weakHardware: false);

            Assert.NotNull(reason);
            Assert.False(AnimationPolicy.IsEnabled(userEnabled: true, systemBlockReason: reason));
        }

        [Fact]
        public void PcFracoBloqueia()
        {
            var reason = AnimationPolicy.SystemBlockReason(clientAreaAnimation: true, remoteSession: false, weakHardware: true);

            Assert.NotNull(reason);
            Assert.False(AnimationPolicy.IsEnabled(userEnabled: true, systemBlockReason: reason));
        }

        [Theory]
        [InlineData(0x10000, 8, true)]   // gráficos abaixo do Tier 2
        [InlineData(0x20000, 4, false)]  // Tier 2 com quatro núcleos
        [InlineData(0x20000, 2, true)]   // dois núcleos ou menos
        [InlineData(0x20000, 1, true)]
        [InlineData(0x30000, 8, false)]
        public void PcFracoPeloTierDeRenderizacaoOuNucleos(int renderTier, int processors, bool expected)
        {
            Assert.Equal(expected, AnimationPolicy.IsWeakHardware(renderTier, processors));
        }

        [Fact]
        public void MotivoDoSistemaTemPrioridadeSobreAcessoRemotoEPcFraco()
        {
            // Windows com efeitos desligados é o primeiro motivo a aparecer ao usuário
            var reason = AnimationPolicy.SystemBlockReason(clientAreaAnimation: false, remoteSession: true, weakHardware: true);

            Assert.Equal("Desativadas pelo Windows (efeitos de animação)", reason);
        }
    }
}
