using System;
using System.IO;
using System.Threading;
using OtimizaPC.Cleaning.Files;
using Xunit;

namespace OtimizaPC.Tests
{
    public class FileCleaningTests
    {
        [Fact]
        public void Scan_RespeitaIdadeMinima()
        {
            using (var temp = new TempFolder())
            {
                var old = temp.File("velho.tmp", 1000, TimeSpan.FromDays(3));
                var recent = temp.File("novo.tmp", 500);

                var scan = FileScanner.Scan(new[] { new FileTarget(temp.Path, minAge: TimeSpan.FromDays(1)) }, CancellationToken.None);

                Assert.Contains(old, scan.Files);
                Assert.DoesNotContain(recent, scan.Files);
                Assert.Equal(1000, scan.Bytes);
            }
        }

        [Fact]
        public void Scan_NaoSegueJunction()
        {
            using (var outside = new TempFolder())
            using (var temp = new TempFolder())
            {
                var precious = outside.File("importante.docx", 2000);
                temp.File("lixo.tmp", 10);
                temp.Junction("link", outside.Path);
                Assert.True(Directory.Exists(Path.Combine(temp.Path, "link")), "junction não foi criada");

                var scan = FileScanner.Scan(new[] { new FileTarget(temp.Path) }, CancellationToken.None);
                FileDeleter.Delete(scan, CancellationToken.None);

                Assert.DoesNotContain(precious, scan.Files);
                Assert.True(File.Exists(precious), "arquivo fora da pasta alvo foi apagado através da junction");
            }
        }

        [Fact]
        public void Scan_IgnoraRaizQueEJunction()
        {
            using (var outside = new TempFolder())
            using (var temp = new TempFolder())
            {
                var precious = outside.File("importante.docx", 2000);
                temp.Junction("link", outside.Path);

                var scan = FileScanner.Scan(new[] { new FileTarget(Path.Combine(temp.Path, "link")) }, CancellationToken.None);

                Assert.Empty(scan.Files);
                Assert.True(File.Exists(precious));
            }
        }

        [Fact]
        public void Scan_NaoRecursivoComFiltro()
        {
            using (var temp = new TempFolder())
            {
                var match = temp.File("thumbcache_256.db");
                var other = temp.File("outro.db");
                var nested = temp.File(@"sub\thumbcache_32.db");

                var scan = FileScanner.Scan(new[] { new FileTarget(temp.Path, "thumbcache_*.db", recursive: false) }, CancellationToken.None);

                Assert.Contains(match, scan.Files);
                Assert.DoesNotContain(other, scan.Files);
                Assert.DoesNotContain(nested, scan.Files);
            }
        }

        // ---------- Alvo de arquivo específico (ex.: C:\Windows\MEMORY.DMP) ----------

        [Fact]
        public void FileTarget_DetectaSomenteNomeDeArquivoExato()
        {
            Assert.True(new FileTarget(@"%WINDIR%", "MEMORY.DMP", recursive: false).IsSingleFile);
            Assert.False(new FileTarget(@"%WINDIR%", "*.dmp", recursive: false).IsSingleFile);
            Assert.False(new FileTarget(@"%WINDIR%", "MEMORY.DMP").IsSingleFile);
            // A idade mínima não interfere na identificação do alvo
            Assert.True(new FileTarget(@"%WINDIR%", "MEMORY.DMP", recursive: false, minAge: TimeSpan.FromDays(1)).IsSingleFile);
            // Nome com pasta não é arquivo único: impediria a checagem de caminho protegido
            Assert.False(new FileTarget(@"%WINDIR%", @"Minidump\MEMORY.DMP", recursive: false).IsSingleFile);
            Assert.False(new FileTarget(@"%WINDIR%", @"..\MEMORY.DMP", recursive: false).IsSingleFile);
        }

        [Fact]
        public void Scan_ArquivoUnicoEncontraSomenteOArquivo()
        {
            using (var temp = new TempFolder())
            {
                var memory = temp.File("MEMORY.DMP", 4096);
                temp.File("outro.dmp", 100);
                temp.File(@"sub\MEMORY.DMP", 100);

                var scan = FileScanner.Scan(new[] { new FileTarget(temp.Path, "MEMORY.DMP", recursive: false) }, CancellationToken.None);

                Assert.Equal(new[] { memory }, scan.Files);
                Assert.Equal(4096, scan.Bytes);
                Assert.Equal(1, scan.ItemCount);
                Assert.Empty(scan.FolderRoots);
            }
        }

        [Fact]
        public void Scan_ArquivoUnicoRespeitaIdadeMinima()
        {
            using (var temp = new TempFolder())
            {
                var old = temp.File("MEMORY.DMP", 2048, TimeSpan.FromDays(3));

                var recent = FileScanner.Scan(new[] { new FileTarget(temp.Path, "MEMORY.DMP", recursive: false, minAge: TimeSpan.FromDays(1)) }, CancellationToken.None);
                Assert.Contains(old, recent.Files);

                File.SetLastWriteTimeUtc(old, DateTime.UtcNow);
                var tooNew = FileScanner.Scan(new[] { new FileTarget(temp.Path, "MEMORY.DMP", recursive: false, minAge: TimeSpan.FromDays(1)) }, CancellationToken.None);
                Assert.Empty(tooNew.Files);
                Assert.Equal(0, tooNew.Bytes);
            }
        }

        [Fact]
        public void Scan_ArquivoUnicoInexistenteNaoRetornaNada()
        {
            using (var temp = new TempFolder())
            {
                temp.File("outro.dmp", 100);

                var scan = FileScanner.Scan(new[] { new FileTarget(temp.Path, "MEMORY.DMP", recursive: false) }, CancellationToken.None);

                Assert.Empty(scan.Files);
                Assert.Equal(0, scan.Bytes);
            }
        }

        [Fact]
        public void Delete_ArquivoUnicoRemoveSomenteOArquivoAnalisado()
        {
            using (var temp = new TempFolder())
            {
                var memory = temp.File("MEMORY.DMP", 4096);
                var other = temp.File("outro.dmp", 100);
                var nested = temp.File(@"sub\MEMORY.DMP", 100);

                var scan = FileScanner.Scan(new[] { new FileTarget(temp.Path, "MEMORY.DMP", recursive: false) }, CancellationToken.None);
                var result = FileDeleter.Delete(scan, CancellationToken.None);

                Assert.Equal(1, result.Deleted);
                Assert.Equal(4096, result.FreedBytes);
                Assert.False(File.Exists(memory));
                Assert.True(File.Exists(other), "arquivo não analisado não pode ser apagado");
                Assert.True(File.Exists(nested), "subpasta não pode ser varrida por um alvo não recursivo");
            }
        }

        [Fact]
        public void Delete_RemoveArquivosESubpastasVaziasMantendoRaiz()
        {
            using (var temp = new TempFolder())
            {
                temp.File(@"a\b\c.tmp", 300);
                var readOnly = temp.File(@"a\somente-leitura.tmp", 200);
                File.SetAttributes(readOnly, FileAttributes.ReadOnly);

                var scan = FileScanner.Scan(new[] { new FileTarget(temp.Path) }, CancellationToken.None);
                var result = FileDeleter.Delete(scan, CancellationToken.None);

                Assert.Equal(2, result.Deleted);
                Assert.Equal(500, result.FreedBytes);
                Assert.True(Directory.Exists(temp.Path), "a pasta raiz deve ser mantida");
                Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
            }
        }

        [Fact]
        public void Delete_ArquivoEmUsoEhIgnoradoSemErro()
        {
            using (var temp = new TempFolder())
            {
                var locked = temp.File("em-uso.tmp", 100);
                var free = temp.File("livre.tmp", 100);

                var scan = FileScanner.Scan(new[] { new FileTarget(temp.Path) }, CancellationToken.None);
                using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var result = FileDeleter.Delete(scan, CancellationToken.None);
                    Assert.Equal(1, result.Deleted);
                    Assert.Equal(1, result.Skipped);
                }

                Assert.True(File.Exists(locked));
                Assert.False(File.Exists(free));
            }
        }

        [Theory]
        [InlineData(@"C:\")]
        [InlineData(@"C:")]
        [InlineData("%WINDIR%")]
        [InlineData(@"%WINDIR%\System32")]
        [InlineData("%USERPROFILE%")]
        [InlineData(@"%USERPROFILE%\Documents")]
        [InlineData(@"%USERPROFILE%\Desktop")]
        [InlineData(@"%USERPROFILE%\Downloads")]
        [InlineData("%ProgramFiles%")]
        [InlineData("%ProgramData%")]
        [InlineData("%LocalAppData%")]
        [InlineData("%AppData%")]
        [InlineData(@"\\servidor\compartilhamento\pasta")]
        [InlineData("")]
        public void SafePath_RecusaPastasProtegidas(string root)
        {
            Assert.False(SafePath.IsAllowed(new FileTarget(root)));
        }

        [Fact]
        public void SafePath_PermiteArquivoEspecificoEmPastaProtegida()
        {
            Assert.True(SafePath.IsAllowed(new FileTarget("%WINDIR%", "MEMORY.DMP", recursive: false)));
            Assert.False(SafePath.IsAllowed(new FileTarget("%WINDIR%", "*.dmp", recursive: false)));
        }

        [Theory]
        [InlineData("%TEMP%")]
        [InlineData(@"%WINDIR%\Temp")]
        [InlineData(@"%WINDIR%\SoftwareDistribution\Download")]
        [InlineData(@"%LocalAppData%\Google\Chrome\User Data\Default\Cache")]
        public void SafePath_PermitePastasDeCache(string root)
        {
            Assert.True(SafePath.IsAllowed(new FileTarget(root)));
        }

        [Fact]
        public void Scan_AlvoProtegidoNaoRetornaNada()
        {
            var scan = FileScanner.Scan(new[] { new FileTarget(@"%USERPROFILE%\Documents") }, CancellationToken.None);
            Assert.Empty(scan.Files);
        }
    }
}
