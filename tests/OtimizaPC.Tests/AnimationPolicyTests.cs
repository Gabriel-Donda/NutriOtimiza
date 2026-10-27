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
        [InlineData(0x00000, true)]   // renderização por software
        [InlineData(0x10000, false)]  // Tier 1: aceleração parcial já anima bem
        [InlineData(0x20000, false)]
        public void PcFracoSoComRenderizacaoPorSoftware(int renderTier, bool expected)
        {
            Assert.Equal(expected, AnimationPolicy.IsWeakHardware(renderTier));
        }

        [Fact]
        public void ForcarIgnoraBloqueioDeHardware()
        {
            var reason = AnimationPolicy.SystemBlockReason(clientAreaAnimation: true, remoteSession: false, weakHardware: true, forceAnimations: true);

            Assert.Null(reason);
            Assert.True(AnimationPolicy.IsEnabled(userEnabled: true, systemBlockReason: reason));
        }

        [Theory]
        [InlineData(false, false)] // Windows sem animações
        [InlineData(true, true)]   // acesso remoto
        public void ForcarNaoIgnoraWindowsNemAcessoRemoto(bool clientAreaAnimation, bool remoteSession)
        {
            var reason = AnimationPolicy.SystemBlockReason(clientAreaAnimation, remoteSession, weakHardware: false, forceAnimations: true);

            Assert.NotNull(reason);
        }

        [Fact]
        public void MotivoDoSistemaTemPrioridadeSobreAcessoRemotoEPcFraco()
        {
            // Windows com efeitos desligados é o primeiro motivo a aparecer ao usuário
            var reason = AnimationPolicy.SystemBlockReason(clientAreaAnimation: false, remoteSession: true, weakHardware: true);

            Assert.Equal("efeitos de animação desligados no Windows", reason);
        }

        [Fact]
        public void EstadoMostraAtivasOuMotivo()
        {
            Assert.Equal("Animações ativas", AnimationPolicy.StatusText(true, null));
            Assert.Equal("Desativadas: acesso remoto (área de trabalho remota)",
                AnimationPolicy.StatusText(true, AnimationPolicy.SystemBlockReason(true, true, false)));
            Assert.StartsWith("Desativadas:", AnimationPolicy.StatusText(false, null));
        }

        [Fact]
        public void LinhaDeLogTrazOsValoresDaDecisao()
        {
            var line = AnimationPolicy.LogLine(true, null, 0x20000, 8, clientAreaAnimation: true, remoteSession: false, forceAnimations: false);

            Assert.StartsWith("Animações: ativas", line);
            Assert.Contains("RenderTier=2", line);
            Assert.Contains("ProcessorCount=8", line);
            Assert.Contains("ClientAreaAnimation=True", line);
            Assert.Contains("Remoto=False", line);
        }
    }
}
