using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OtimizaPC.Tweaks.Apps;
using Xunit;

namespace OtimizaPC.Tests
{
    public class AppCatalogTests
    {
        private static AppPackage Store(string name, bool nonRemovable = false) =>
            new AppPackage { Name = name, SignatureKind = "Store", NonRemovable = nonRemovable };

        [Theory]
        // Essenciais: quebrariam vídeo, fotos, instalação de apps, segurança ou drivers
        [InlineData("Microsoft.WindowsStore")]
        [InlineData("Microsoft.DesktopAppInstaller")]
        [InlineData("Microsoft.Winget.Source")]
        [InlineData("Microsoft.HEVCVideoExtension")]
        [InlineData("Microsoft.WebMediaExtensions")]
        [InlineData("Microsoft.HEIFImageExtension")]
        [InlineData("Microsoft.SecHealthUI")]
        [InlineData("Microsoft.Windows.Photos")]
        [InlineData("Microsoft.WindowsCalculator")]
        [InlineData("Microsoft.LanguageExperiencePackpt-BR")]
        [InlineData("MicrosoftCorporationII.WinAppRuntime.Main.2")]
        [InlineData("AppUp.IntelGraphicsExperience")]
        [InlineData("Microsoft.XboxIdentityProvider")]
        public void Protege_PacotesEssenciais(string name)
        {
            Assert.True(AppCatalog.IsProtected(Store(name)));
        }

        [Fact]
        public void Protege_SistemaENaoRemovivel()
        {
            Assert.True(AppCatalog.IsProtected(new AppPackage { Name = "Qualquer.App", SignatureKind = "System" }));
            Assert.True(AppCatalog.IsProtected(Store("king.com.CandyCrushSaga", nonRemovable: true)));
        }

        [Theory]
        [InlineData("king.com.CandyCrushSaga", AppRecommendation.Remove)]
        [InlineData("KING.COM.CandyCrushFriends", AppRecommendation.Remove)]
        [InlineData("Microsoft.Windows.DevHome", AppRecommendation.Remove)]
        [InlineData("57540AMZNMobileLLC.AmazonAlexa", AppRecommendation.Remove)]
        [InlineData("Microsoft.BingWeather", AppRecommendation.Optional)]
        [InlineData("4DF9E0F8.Netflix", AppRecommendation.Optional)]
        [InlineData("MicrosoftWindows.Client.WebExperience", AppRecommendation.Optional)]
        public void Catalogo_ReconheceEPermite(string name, AppRecommendation expected)
        {
            var entry = AppCatalog.Find(name);
            Assert.NotNull(entry);
            Assert.Equal(expected, entry.Recommendation);
            Assert.False(AppCatalog.IsProtected(Store(name)));
        }

        [Fact]
        public void AppDeTerceiroForaDoCatalogo_PodeSerRemovido()
        {
            Assert.False(AppCatalog.IsProtected(Store("AD2F1837.HPSupportAssistant")));
            Assert.Null(AppCatalog.Find("AD2F1837.HPSupportAssistant"));
        }

        [Fact]
        public void Catalogo_SemPadroesDuplicados()
        {
            var patterns = AppCatalog.Entries.Select(e => e.Pattern.ToLowerInvariant()).ToList();
            Assert.Equal(patterns.Count, patterns.Distinct().Count());
        }

        [Fact]
        public void Json_AceitaListaEObjetoUnico()
        {
            const string item = "{\"Name\":\"5319275A.WhatsAppDesktop\",\"PackageFullName\":\"5319275A.WhatsAppDesktop_2.0_x64__cv1g1gvanyjgm\"," +
                                "\"Publisher\":\"CN=24803D75-212C-471A-BC57-9EF86AB91435\",\"SignatureKind\":\"Store\",\"NonRemovable\":false," +
                                "\"InstallLocation\":\"C:\\\\Program Files\\\\WindowsApps\\\\x\",\"Version\":\"2.0\"}";

            var list = AppxService.ParsePackages("[" + item + "]");
            var single = AppxService.ParsePackages(item);
            var empty = AppxService.ParsePackages("[]");

            Assert.Single(list);
            Assert.Single(single);
            Assert.Empty(empty);
            Assert.Equal("WhatsAppDesktop", single[0].Name.Split('.').Last());
            Assert.Equal(@"C:\Program Files\WindowsApps\x", single[0].InstallLocation);
        }

        [Theory]
        [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "Microsoft Corporation")]
        [InlineData("CN=\"HP Inc.\", O=\"HP Inc.\"", "HP Inc.")]
        [InlineData("CN=ED346674-0FA1-4272-85CE-3187C9C86E26", null)]
        public void Publicador_ExtraiNomeEIgnoraGuid(string publisher, string expected)
        {
            Assert.Equal(expected, new AppPackage { Publisher = publisher }.PublisherName);
        }

        [Theory]
        [InlineData("AD2F1837.HPSupportAssistant", "HP Support Assistant")]
        [InlineData("Microsoft.PowerAutomateDesktop", "Power Automate Desktop")]
        public void NomeLegivel_SemManifestoSeparaPalavras(string name, string expected)
        {
            Assert.Equal(expected, new AppPackage { Name = name }.DisplayName);
        }

        [Fact]
        public void NomeLegivel_PrefereManifesto()
        {
            Assert.Equal("WhatsApp", new AppPackage { Name = "5319275A.WhatsAppDesktop", ManifestDisplayName = "WhatsApp" }.DisplayName);
        }

        [Fact]
        public void Removidos_SaemDaListaQuandoReinstalados()
        {
            var file = Path.Combine(Path.GetTempPath(), "OtimizaPC.Tests", Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var service = new AppxService(file);
                service.SaveRemoved(new List<RemovedApp>
                {
                    new RemovedApp { Name = "A.Volta", DisplayName = "Volta" },
                    new RemovedApp { Name = "B.Continua", DisplayName = "Continua" },
                });

                service.SyncRemoved(new[] { Store("A.Volta") });

                var remaining = new AppxService(file).LoadRemoved();
                Assert.Equal(new[] { "B.Continua" }, remaining.Select(r => r.Name));
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }
}
