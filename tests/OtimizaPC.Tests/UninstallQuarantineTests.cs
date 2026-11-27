using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using OtimizaPC.Tweaks.Uninstall;
using Xunit;

namespace OtimizaPC.Tests
{
    public class UninstallQuarantineTests
    {
        [Fact]
        public void ManifestRoundTripsOriginalPathsKindsDateAndRegistryView()
        {
            var created = new DateTime(2026, 11, 27, 12, 30, 45, DateTimeKind.Utc);
            var manifest = new UninstallQuarantineManifest
            {
                ProgramName = "Edição de áudio",
                UserSid = "S-1-5-18",
                CreatedUtc = created,
                Items = new List<QuarantinedLeftover>
                {
                    new QuarantinedLeftover
                    {
                        Kind = LeftoverKind.Shortcut,
                        OriginalPath = @"C:\Desktop\Áudio.lnk",
                        BackupName = "000000.lnk"
                    },
                    new QuarantinedLeftover
                    {
                        Kind = LeftoverKind.RegistryKey,
                        OriginalPath = @"HKLM\SOFTWARE\WOW6432Node\Áudio",
                        BackupName = "000001.reg",
                        Hive = RegistryHive.LocalMachine,
                        View = RegistryView.Registry32,
                        RegistryPath = @"SOFTWARE\Áudio"
                    }
                }
            };

            using (var stream = new MemoryStream())
            {
                manifest.Write(stream);
                stream.Position = 0;
                var restored = UninstallQuarantineManifest.Read(stream);
                Assert.Equal(manifest.ProgramName, restored.ProgramName);
                Assert.Equal(manifest.UserSid, restored.UserSid);
                Assert.Equal(created, restored.CreatedUtc);
                Assert.Equal(DateTimeKind.Utc, restored.CreatedUtc.Kind);
                Assert.Collection(restored.Items,
                    item =>
                    {
                        Assert.Equal(LeftoverKind.Shortcut, item.Kind);
                        Assert.Equal(manifest.Items[0].OriginalPath, item.OriginalPath);
                        Assert.Equal("000000.lnk", item.BackupName);
                    },
                    item =>
                    {
                        Assert.Equal(LeftoverKind.RegistryKey, item.Kind);
                        Assert.Equal(manifest.Items[1].OriginalPath, item.OriginalPath);
                        Assert.Equal("000001.reg", item.BackupName);
                        Assert.Equal(RegistryHive.LocalMachine, item.Hive);
                        Assert.Equal(RegistryView.Registry32, item.View);
                        Assert.Equal(@"SOFTWARE\Áudio", item.RegistryPath);
                    });
            }
        }

        [Theory]
        [InlineData(" .. ", "programa")]
        [InlineData(null, "programa")]
        [InlineData(" ..Áudio: A/B\\C? . ", "Áudio_ A_B_C_")]
        public void SanitizesOperationName(string name, string expected)
        {
            Assert.Equal(expected, UninstallQuarantine.SanitizeName(name));
        }

        [Fact]
        public void LimitsOperationNameLength()
        {
            Assert.Equal(new string('a', 60), UninstallQuarantine.SanitizeName(new string('a', 100)));
        }

        [Fact]
        public void ExpiresOnlyAfterSevenFullDays()
        {
            var created = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
            Assert.False(UninstallQuarantine.IsExpired(created, created.AddDays(6)));
            Assert.False(UninstallQuarantine.IsExpired(created, created.AddDays(7)));
            Assert.True(UninstallQuarantine.IsExpired(created, created.AddDays(7).AddTicks(1)));
        }

        [Fact]
        public void BlocksCleanupWhenUninstallKeyStillExists()
        {
            var program = new InstalledProgram
            {
                DisplayName = "Studio",
                Hive = RegistryHive.CurrentUser,
                View = RegistryView.Registry32,
                SubKeyName = "studio-entry"
            };
            var scanner = CreateScanner(new TestFileSystem(), new TestRegistry());
            var quarantine = new UninstallQuarantine(scanner);

            var result = quarantine.Remove(program, new[]
            {
                new UninstallLeftover(LeftoverKind.Folder, @"C:\Apps\Studio", LeftoverConfidence.High)
            });

            Assert.True(result.Blocked);
            Assert.Equal(0, result.Completed);
            Assert.Contains(UninstallQuarantineResult.StillInstalledMessage, Assert.Single(result.Skipped));
        }

        [Theory]
        [InlineData(@"C:\Apps\Studio", null)]
        [InlineData(@"C:\Apps\Studio", FileAttributes.Normal)]
        [InlineData(@"C:\Apps\Studio", FileAttributes.Directory | FileAttributes.ReparsePoint)]
        [InlineData(@"C:\Apps", FileAttributes.Directory | FileAttributes.ReparsePoint)]
        public void RevalidatesSourceAndItsParentsBeforeMoving(string path, FileAttributes? changedAttributes)
        {
            const string source = @"C:\Apps\Studio";
            var files = new TestFileSystem();
            var scanner = CreateScanner(files, new TestRegistry());
            var leftover = Assert.Single(scanner.Scan(new InstalledProgram
            {
                DisplayName = "Studio",
                InstallLocation = source,
                SubKeyName = "studio-entry"
            }));
            var safeDirectory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
            var backup = Path.Combine(safeDirectory, "unused-backup");
            var moved = false;
            UninstallQuarantineFiles.Move(scanner, leftover.Kind, source, backup, safeDirectory,
                restore: false, move: (from, to) =>
                {
                    Assert.Equal(source, from);
                    Assert.Equal(backup, to);
                    moved = true;
                });
            Assert.True(moved);

            files.Attributes[path] = changedAttributes;
            Assert.Throws<IOException>(() => UninstallQuarantineFiles.Move(scanner, leftover.Kind, source, backup,
                safeDirectory, restore: false, move: (from, to) => throw new InvalidOperationException("Move não esperado.")));
        }

        private static LeftoverScanner CreateScanner(TestFileSystem files, TestRegistry registry) =>
            new LeftoverScanner(files, registry, @"C:\Windows", new[] { @"C:\Apps" },
                Array.Empty<string>(), Array.Empty<string>(), is64Bit: false);

        private sealed class TestRegistry : ILeftoverRegistry
        {
            public bool KeyExists(RegistryHive hive, RegistryView view, string path) =>
                hive == RegistryHive.CurrentUser && view == RegistryView.Registry32 &&
                path == InstalledProgramService.UninstallPath + @"\studio-entry";

            public IEnumerable<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string path) =>
                Array.Empty<string>();
        }

        private sealed class TestFileSystem : ILeftoverFileSystem
        {
            public Dictionary<string, FileAttributes?> Attributes { get; } = new Dictionary<string, FileAttributes?>
            {
                [@"C:\"] = FileAttributes.Directory,
                [@"C:\Apps"] = FileAttributes.Directory,
                [@"C:\Apps\Studio"] = FileAttributes.Directory
            };

            public FileAttributes? GetAttributes(string path) =>
                Attributes.TryGetValue(path, out var attributes) ? attributes : null;

            public IEnumerable<string> GetDirectories(string path) => Array.Empty<string>();
            public IEnumerable<string> GetFiles(string path) => Array.Empty<string>();
        }
    }
}
