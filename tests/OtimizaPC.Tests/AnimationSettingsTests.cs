using System.IO;
using OtimizaPC.Core.Settings;
using Xunit;

namespace OtimizaPC.Tests
{
    public class AnimationSettingsTests
    {
        [Fact]
        public void AnimacoesComecamLigadasPorPadrao()
        {
            Assert.True(new AppSettings().AnimationsEnabled);
        }

        [Fact]
        public void ConfiguracaoAntigaSemCampoDeAnimacoesCarregaComoLigada()
        {
            using (var temp = new TempFolder())
            {
                // settings.json gravado antes da existência do campo AnimationsEnabled
                var path = Path.Combine(temp.Path, "settings.json");
                File.WriteAllText(path, "{\"CheckUpdatesOnStartup\":false}");

                var settings = new SettingsStore(path).Load();

                Assert.True(settings.AnimationsEnabled);
                Assert.False(settings.CheckUpdatesOnStartup);
            }
        }

        [Fact]
        public void PreferenciaDesligadaSobreviveAoSalvarECarregar()
        {
            using (var temp = new TempFolder())
            {
                var store = new SettingsStore(Path.Combine(temp.Path, "settings.json"));
                store.Save(new AppSettings { AnimationsEnabled = false });

                Assert.False(store.Load().AnimationsEnabled);
            }
        }
    }
}
