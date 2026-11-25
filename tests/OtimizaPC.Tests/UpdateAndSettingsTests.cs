using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using OtimizaPC.Cleaning;
using OtimizaPC.Core;
using OtimizaPC.Core.Settings;
using OtimizaPC.Core.Updates;
using Xunit;

namespace OtimizaPC.Tests
{
    public class UpdateAndSettingsTests
    {
        private const string Release = @"{
            ""tag_name"": ""v1.2.0"", ""body"": ""Novidades"", ""html_url"": ""https://github.com/x/y/releases/tag/v1.2.0"",
            ""draft"": false, ""prerelease"": false,
            ""assets"": [
                { ""name"": ""NutriOtimiza-Setup-1.2.0-x64.exe"", ""size"": 123, ""browser_download_url"": ""https://example/x64.exe"" },
                { ""name"": ""NutriOtimiza-Setup-1.2.0-x64.exe.sha256"", ""browser_download_url"": ""https://example/x64.sha"" },
                { ""name"": ""NutriOtimiza-Setup-1.2.0-x86.exe"", ""browser_download_url"": ""https://example/x86.exe"" },
                { ""name"": ""NutriOtimiza-Portable-1.2.0-x64.zip"", ""browser_download_url"": ""https://example/zip"" }
            ] }";

        [Fact]
        public void Release_VersaoNovaEscolheInstaladorDaArquitetura()
        {
            var update = UpdateService.ParseRelease(Release, "x64", new Version(1, 0, 0, 0));

            Assert.NotNull(update);
            Assert.Equal(new Version(1, 2, 0, 0), update.Version);
            Assert.Equal("https://example/x64.exe", update.InstallerUrl);
            Assert.Equal((long?)123, update.InstallerSize);
            Assert.Equal("https://example/x64.sha", update.ChecksumUrl);
        }

        [Fact]
        public void Release_SemChecksumParaArquiteturaFicaSemUrlDeVerificacao()
        {
            var update = UpdateService.ParseRelease(Release, "x86", new Version(1, 0, 0, 0));
            Assert.Equal("https://example/x86.exe", update.InstallerUrl);
            Assert.Null(update.InstallerSize);
            Assert.Null(update.ChecksumUrl); // o download será recusado
        }

        [Theory]
        [InlineData(1, 2, 0)]
        [InlineData(1, 3, 0)]
        public void Release_MesmaVersaoOuAnteriorNaoEhAtualizacao(int major, int minor, int build)
        {
            Assert.Null(UpdateService.ParseRelease(Release, "x64", new Version(major, minor, build, 0)));
        }

        [Fact]
        public void Release_RascunhoEPreviaSaoIgnorados()
        {
            Assert.Null(UpdateService.ParseRelease(Release.Replace("\"draft\": false", "\"draft\": true"), "x64", new Version(1, 0, 0, 0)));
            Assert.Null(UpdateService.ParseRelease(Release.Replace("\"prerelease\": false", "\"prerelease\": true"), "x64", new Version(1, 0, 0, 0)));
        }

        [Theory]
        [InlineData("v1.2.3", "1.2.3.0")]
        [InlineData("1.2", "1.2.0.0")]
        [InlineData("NutriOtimiza 2.0.1", "2.0.1.0")]
        public void Versao_InterpretaTags(string tag, string expected)
        {
            Assert.Equal(Version.Parse(expected), UpdateService.ParseVersion(tag));
        }

        [Fact]
        public void Checksum_AceitaFormatoSha256sumERejeitaInvalido()
        {
            var hash = new string('a', 64);
            Assert.Equal(hash, UpdateService.ParseChecksum(hash.ToUpperInvariant() + "  NutriOtimiza-Setup-1.2.0-x64.exe\n"));
            Assert.Throws<InvalidOperationException>(() => UpdateService.ParseChecksum("não é um hash"));
        }

        [Fact]
        public void Checksum_CalculaSha256DoArquivo()
        {
            var file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, "abc");
                Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", UpdateService.ComputeSha256(file));
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Theory]
        [InlineData("NutriOtimiza-Setup-1.2.0-x64.exe", true)]
        [InlineData("NutriOtimiza-Setup-1.2.0.0-x64.exe", true)]
        [InlineData("Outro-Setup-1.2.0-x64.exe", false)]
        [InlineData("NutriOtimiza-Setup-1.2.1-x64.exe", false)]
        [InlineData("NutriOtimiza-Setup-1.2.0-x86.exe", false)]
        [InlineData("NutriOtimiza-Setup-1.2.0-arm64.exe", false)]
        [InlineData("../NutriOtimiza-Setup-1.2.0-x64.exe", false)]
        public void Instalador_ConfereNomeVersaoEArquitetura(string name, bool valid)
        {
            Assert.Equal(valid, UpdateService.IsInstallerNameValid(name, new Version(1, 2, 0, 0), "x64"));
        }

        [Theory]
        [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", 3, true)]
        [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", null, true)]
        [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", 4, false)]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000000", 3, false)]
        public void Instalador_ConfereHashETamanho(string checksum, int? size, bool valid)
        {
            using (var stream = new MemoryStream(new byte[] { 97, 98, 99 }))
            {
                if (valid)
                    UpdateService.ValidateInstallerContents(stream, checksum, size);
                else
                    Assert.Throws<InvalidOperationException>(() => UpdateService.ValidateInstallerContents(stream, checksum, size));
            }
        }

        [Fact]
        public void Instalador_RecusaArquivoAlteradoDepoisDaVerificacaoELimpaPasta()
        {
            var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var architecture = UpdateService.Architecture;
            var path = Path.Combine(folder, $"NutriOtimiza-Setup-1.2.0-{architecture}.exe");
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(path, "abc");
                var checksum = UpdateService.ComputeSha256(path);
                var installer = new StagedInstaller(path, new Version(1, 2, 0), architecture, checksum, 3);
                using (var stream = UpdateService.OpenInstallerForValidation(path))
                    UpdateService.ValidateInstallerContents(stream, checksum, 3);

                // Mantém o tamanho para que a alteração seja detectada pelo hash.
                File.WriteAllText(path, "abd");

                var exception = Assert.Throws<InvalidOperationException>(() => UpdateService.LaunchInstaller(installer, relaunch: false));
                Assert.Contains("hash publicado", exception.Message);
                Assert.False(Directory.Exists(folder));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
        }

        [Theory]
        [InlineData("dono/repo", true)]
        [InlineData("minha-empresa/otimiza.pc", true)]
        [InlineData("https://github.com/dono/repo", false)]
        [InlineData("dono", false)]
        [InlineData("", false)]
        public void Repositorio_ValidaFormato(string repository, bool valid)
        {
            Assert.Equal(valid, UpdateService.IsValidRepository(repository));
        }

        [Fact]
        public void Configuracoes_PersistemEUsamPadraoParaArquivoAntigo()
        {
            var file = Path.Combine(Path.GetTempPath(), "OtimizaPC.Tests", Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new SettingsStore(file);
                store.Save(new AppSettings { Theme = ThemePreference.Dark, CheckUpdatesOnStartup = false, UpdateRepository = "a/b", CollapsedSensorGroups = new System.Collections.Generic.List<string> { "cpu-0" }, CollapsedHardwareSections = new System.Collections.Generic.List<string> { "memory", "gpu" }, CollapsedCleanupGroups = new System.Collections.Generic.List<string> { "Navegadores" } });
                var loaded = store.Load();
                Assert.Equal(ThemePreference.Dark, loaded.Theme);
                Assert.False(loaded.CheckUpdatesOnStartup);
                Assert.Equal("a/b", loaded.UpdateRepository);
                Assert.Equal(new[] { "cpu-0" }, loaded.CollapsedSensorGroups);
                Assert.Equal(new[] { "memory", "gpu" }, loaded.CollapsedHardwareSections);
                Assert.Equal(new[] { "Navegadores" }, loaded.CollapsedCleanupGroups);

                // Arquivo de uma versão anterior, sem os campos novos
                File.WriteAllText(file, "{}");
                var old = store.Load();
                Assert.True(old.CheckUpdatesOnStartup);
                Assert.Equal(string.Empty, old.UpdateRepository);
                Assert.Empty(old.CollapsedSensorGroups);
                Assert.Empty(old.CollapsedHardwareSections);
                Assert.Empty(old.CollapsedCleanupGroups);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        [Fact]
        public void Configuracoes_SalvamArquivoSemDiretorio()
        {
            var file = "OtimizaPC.Tests-" + Guid.NewGuid().ToString("N") + ".json";
            try
            {
                var store = new SettingsStore(file);
                store.Save(new AppSettings { Theme = ThemePreference.Light, CheckUpdatesOnStartup = false });

                Assert.True(File.Exists(file));
                var loaded = store.Load();
                Assert.Equal(ThemePreference.Light, loaded.Theme);
                Assert.False(loaded.CheckUpdatesOnStartup);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
                if (File.Exists(file + ".tmp")) File.Delete(file + ".tmp");
            }
        }

        [Fact]
        public void RepositorioEfetivo_VazioUsaPadraoEPreenchidoUsaValorAparado()
        {
            Assert.Equal(AppInfo.DefaultUpdateRepository, new AppSettings { UpdateRepository = "   " }.EffectiveUpdateRepository);
            Assert.Equal("a/b", new AppSettings { UpdateRepository = "  a/b " }.EffectiveUpdateRepository);
        }

        [Fact]
        public void TarefaAgendada_XmlValidoComArgumentoEProximoDomingo()
        {
            var wednesday = new DateTime(2026, 10, 21, 9, 0, 0);
            var xml = XDocument.Parse(ScheduledCleanup.BuildTaskXml(@"C:\Program Files\NutriOtimiza\NutriOtimiza.exe", wednesday));
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

            Assert.Equal(@"C:\Program Files\NutriOtimiza\NutriOtimiza.exe", xml.Descendants(ns + "Command").Single().Value);
            Assert.Equal(AutoCleanup.Argument, xml.Descendants(ns + "Arguments").Single().Value);
            Assert.Equal("2026-10-25T12:00:00", xml.Descendants(ns + "StartBoundary").Single().Value);
            Assert.Equal("true", xml.Descendants(ns + "StartWhenAvailable").Single().Value);
        }

        [Fact]
        public void TarefaAgendada_TimeoutEncerraProcessoEInformaErro()
        {
            var command = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var startInfo = new System.Diagnostics.ProcessStartInfo(command, "/c ping -n 10 127.0.0.1 > nul")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                Assert.Throws<TimeoutException>(() => ScheduledCleanup.WaitForExitOrThrow(process, 100));
                Assert.True(process.HasExited);
            }
        }
    }
}
