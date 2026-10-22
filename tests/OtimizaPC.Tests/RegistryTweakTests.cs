using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using OtimizaPC.Tweaks;
using OtimizaPC.Tweaks.Registry;
using OtimizaPC.Tweaks.Startup;
using Xunit;

namespace OtimizaPC.Tests
{
    /// <summary>Testes numa chave isolada em HKCU\Software\OtimizaPC.Tests, removida ao final.</summary>
    public sealed class RegistryTweakTests : IDisposable
    {
        private const string TestRoot = @"Software\OtimizaPC.Tests";
        private readonly string _base = TestRoot + @"\" + Guid.NewGuid().ToString("N");
        private readonly string _undoFile = Path.Combine(Path.GetTempPath(), "OtimizaPC.Tests", Guid.NewGuid().ToString("N") + ".json");

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(_base, false);
            using (var root = Registry.CurrentUser.OpenSubKey(TestRoot))
            {
                if (root != null && root.SubKeyCount == 0 && root.ValueCount == 0)
                    Registry.CurrentUser.DeleteSubKey(TestRoot, false);
            }
            if (File.Exists(_undoFile)) File.Delete(_undoFile);
        }

        private RegistryTweak Tweak(params RegistryChange[] changes)
        {
            var tweak = new RegistryTweak("teste", "Teste", "Teste", TweakCategories.Interface);
            tweak.Changes.AddRange(changes);
            return tweak;
        }

        /// <summary>Mesmo fluxo do TweakService: prepara, salva em disco, aplica.</summary>
        private UndoRecord ApplyWithUndo(Tweak tweak)
        {
            var store = new UndoStore(_undoFile);
            var record = store.GetOrCreate(tweak.Id);
            tweak.Prepare(record);
            store.Save(record);
            tweak.Apply(record);
            return record;
        }

        private object Read(string path, string name)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(path))
                return key?.GetValue(name);
        }

        private void Write(string path, string name, object value, RegistryValueKind kind)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(path))
                key.SetValue(name, value, kind);
        }

        [Fact]
        public void Reverter_RestauraValorOriginal()
        {
            var path = _base + @"\Existente";
            Write(path, "Valor", 7, RegistryValueKind.DWord);
            var tweak = Tweak(RegistryChange.Dword(RegistryHive.CurrentUser, path, "Valor", 0, 1));

            var record = ApplyWithUndo(tweak);
            Assert.Equal(0, Read(path, "Valor"));
            Assert.Equal(TweakState.Applied, tweak.GetState());

            tweak.Revert(record);
            Assert.Equal(7, Read(path, "Valor")); // o original (7), não o padrão (1)
        }

        [Fact]
        public void Reverter_RemoveValorEChavesCriadas()
        {
            var created = _base + @"\Nova";
            var path = created + @"\Sub";
            var tweak = Tweak(RegistryChange.Dword(RegistryHive.CurrentUser, path, "Valor", 1));

            var record = ApplyWithUndo(tweak);
            Assert.Equal(1, Read(path, "Valor"));

            tweak.Revert(record);
            using (var key = Registry.CurrentUser.OpenSubKey(created))
                Assert.Null(key);
        }

        [Fact]
        public void Reverter_PreservaValoresDeOutrosProgramasNaChaveCriada()
        {
            var created = _base + @"\Politica";
            var tweak = Tweak(RegistryChange.Dword(RegistryHive.CurrentUser, created, "Nosso", 1));

            var record = ApplyWithUndo(tweak);
            Write(created, "DeOutroPrograma", "x", RegistryValueKind.String);

            tweak.Revert(record);
            Assert.Null(Read(created, "Nosso"));
            Assert.Equal("x", Read(created, "DeOutroPrograma"));
        }

        [Fact]
        public void Reaplicar_NaoPerdeOriginal()
        {
            var path = _base + @"\Reaplicar";
            Write(path, "Valor", "original", RegistryValueKind.String);
            var tweak = Tweak(RegistryChange.String(RegistryHive.CurrentUser, path, "Valor", "novo"));

            ApplyWithUndo(tweak);
            var record = ApplyWithUndo(tweak); // segunda aplicação não pode gravar "novo" como original

            tweak.Revert(record);
            Assert.Equal("original", Read(path, "Valor"));
        }

        [Fact]
        public void Reverter_SemRegistroUsaPadraoDoWindows()
        {
            var path = _base + @"\SemRegistro";
            var tweak = Tweak(
                RegistryChange.Dword(RegistryHive.CurrentUser, path, "ComPadrao", 0, 1),
                RegistryChange.Dword(RegistryHive.CurrentUser, path, "SemPadrao", 1));
            tweak.Apply(new UndoRecord("teste"));

            tweak.Revert(null);
            Assert.Equal(1, Read(path, "ComPadrao"));
            Assert.Null(Read(path, "SemPadrao"));
        }

        [Fact]
        public void Reverter_SemRegistroRemoveChaveDoMenuClassico()
        {
            var top = _base + @"\CLSID\{teste}";
            var tweak = Tweak(new RegistryChange(RegistryHive.CurrentUser, top + @"\InprocServer32", string.Empty, string.Empty, RegistryValueKind.String)
            {
                DefaultDeleteKey = top,
            });
            tweak.Apply(new UndoRecord("teste"));
            Assert.Equal(TweakState.Applied, tweak.GetState());

            tweak.Revert(null);
            using (var key = Registry.CurrentUser.OpenSubKey(top))
                Assert.Null(key);
        }

        [Fact]
        public void Estado_Parcial()
        {
            var path = _base + @"\Parcial";
            Write(path, "A", 1, RegistryValueKind.DWord);
            var tweak = Tweak(
                RegistryChange.Dword(RegistryHive.CurrentUser, path, "A", 1),
                RegistryChange.Dword(RegistryHive.CurrentUser, path, "B", 1));

            Assert.Equal(TweakState.Partial, tweak.GetState());
        }

        [Fact]
        public void UndoStore_PersisteTodosOsTiposEntreSessoes()
        {
            var path = _base + @"\Tipos";
            Write(path, "Bin", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
            Write(path, "Multi", new[] { "a", "b" }, RegistryValueKind.MultiString);
            Write(path, "Q", 1234567890123L, RegistryValueKind.QWord);
            Write(path, "Exp", "%TEMP%\\x", RegistryValueKind.ExpandString);
            var tweak = Tweak(
                new RegistryChange(RegistryHive.CurrentUser, path, "Bin", new byte[] { 9 }, RegistryValueKind.Binary),
                new RegistryChange(RegistryHive.CurrentUser, path, "Multi", new[] { "z" }, RegistryValueKind.MultiString),
                new RegistryChange(RegistryHive.CurrentUser, path, "Q", 0L, RegistryValueKind.QWord),
                RegistryChange.String(RegistryHive.CurrentUser, path, "Exp", "x"));

            ApplyWithUndo(tweak);

            // Nova instância: simula reabrir o app
            var reloaded = new UndoStore(_undoFile).Get("teste");
            tweak.Revert(reloaded);

            Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])Read(path, "Bin"));
            Assert.Equal(new[] { "a", "b" }, (string[])Read(path, "Multi"));
            Assert.Equal(1234567890123L, Read(path, "Q"));
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                Assert.Equal(RegistryValueKind.ExpandString, key.GetValueKind("Exp"));
                Assert.Equal("%TEMP%\\x", key.GetValue("Exp", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            }
        }

        [Fact]
        public void Catalogo_IdsUnicosEItensValidos()
        {
            var all = TweakCatalog.Create();
            Assert.NotEmpty(all);
            Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
            Assert.All(all, t =>
            {
                Assert.False(string.IsNullOrWhiteSpace(t.Name));
                Assert.False(string.IsNullOrWhiteSpace(t.Description));
                Assert.Contains(t.Category, TweakCategories.Order);
                if (t is RegistryTweak reg) Assert.NotEmpty(reg.Changes);
            });
        }

        [Theory]
        [InlineData("\"C:\\Program Files\\App\\app.exe\" --minimized", @"C:\Program Files\App\app.exe")]
        [InlineData(@"C:\Tools\tool.exe /background", @"C:\Tools\tool.exe")]
        [InlineData(@"C:\Program Files\Sem Aspas\prog.exe -x", @"C:\Program Files\Sem Aspas\prog.exe")]
        [InlineData("rundll32.exe shell32.dll,Control_RunDLL", "rundll32.exe")]
        public void Inicializacao_ExtraiExecutavel(string command, string expected)
        {
            Assert.Equal(expected, StartupManager.ExtractExecutable(command));
        }

        [Fact]
        public void Inicializacao_FormatoDoGerenciadorDeTarefas()
        {
            var enabled = StartupManager.ApprovedValue(true);
            var disabled = StartupManager.ApprovedValue(false);
            Assert.Equal(12, enabled.Length);
            Assert.Equal(2, enabled[0]);
            Assert.Equal(3, disabled[0]);
            Assert.NotEqual(0L, BitConverter.ToInt64(disabled, 4)); // data em que foi desativado
        }
    }
}
