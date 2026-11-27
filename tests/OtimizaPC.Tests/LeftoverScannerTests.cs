using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using OtimizaPC.Tweaks.Uninstall;
using Xunit;

namespace OtimizaPC.Tests
{
    public class LeftoverScannerTests
    {
        [Theory]
        [InlineData("  Acme Studio 2.3.1 (x64) ", "Acme Studio")]
        [InlineData("Acme Studio (x86) v2.4", "Acme Studio")]
        [InlineData("Acme Studio (64-bit, 2.4)", "Acme Studio")]
        [InlineData("Acme Studio (Versão 2.4 x64)", "Acme Studio")]
        [InlineData("Acme Studio (Editor)", "Acme Studio (Editor)")]
        [InlineData("Acme 7-Zip", "Acme 7-Zip")]
        public void CleansVersionAndArchitectureSuffixes(string name, string expected)
        {
            Assert.Equal(expected, LeftoverScanner.CleanDisplayName(name));
        }

        [Theory]
        [InlineData("Acme, Inc.")]
        [InlineData("Acme Ltd.")]
        [InlineData("Acme LLC")]
        [InlineData("Acme GmbH")]
        [InlineData("Acme Corporation")]
        [InlineData("Acme Co., Ltd.")]
        [InlineData("Acme S.A.")]
        public void CleansPublisherSuffixes(string publisher)
        {
            Assert.Equal("Acme", LeftoverScanner.CleanPublisher(publisher));
        }

        [Theory]
        [InlineData("Microsoft", "Microsoft Corporation")]
        [InlineData("windows 11", "Google LLC")]
        [InlineData("AMD", "Intel, Inc.")]
        [InlineData("NVIDIA (x64)", "Software Ltd.")]
        [InlineData("Common Files", "System GmbH")]
        [InlineData("Update", "Program Co.")]
        [InlineData("Driver", "ABC")]
        public void RejectsGenericAndShortTerms(string name, string publisher)
        {
            Assert.Null(LeftoverScanner.CleanDisplayName(name));
            Assert.Null(LeftoverScanner.CleanPublisher(publisher));
        }

        [Fact]
        public void FindsOnlyExactMatchesAtAllowedDepthsAndReportsConfidence()
        {
            var files = new FileSystem();
            files.Add(@"C:\Program Files\studio");
            files.Add(@"C:\Program Files\Studio Backup");
            files.Add(@"C:\Program Files\acme\STUDIO");
            files.Add(@"C:\Program Files\Acme\Studio Tools");
            files.Add(@"C:\Program Files\Acme\Nested\Studio");
            files.Add(@"C:\Program Files\Other\Studio");
            files.Add(@"C:\Program Files\InstallFolder");
            files.Add(@"C:\Menu\Programs\Studio\Studio.lnk", FileAttributes.Normal);
            files.Add(@"C:\Desktop\STUDIO.LNK", FileAttributes.Normal);
            files.Add(@"C:\Desktop\Studio Plus.lnk", FileAttributes.Normal);
            files.Add(@"C:\Desktop\Studio.url", FileAttributes.Normal);
            files.Add(@"C:\Desktop\Nested\Studio.lnk", FileAttributes.Normal);
            var registry = new Registry();
            registry.Add(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Studio");
            registry.Add(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Studio Plus");
            registry.Add(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\ACME\Studio");
            registry.Add(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\ACME\Other\Studio");
            registry.Add(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Studio");
            registry.Add(RegistryHive.CurrentUser, RegistryView.Registry64, InstalledProgramService.UninstallPath + @"\studio-entry");
            var program = Program(@"C:\Program Files\InstallFolder");

            var scanner = Scanner(files, registry);
            Assert.True(scanner.UninstallKeyExists(program));
            var results = scanner.Scan(program).ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);

            Assert.Equal(10, results.Count);
            AssertItem(results, @"C:\Program Files\Studio", LeftoverKind.Folder, LeftoverConfidence.High);
            AssertItem(results, @"C:\Program Files\InstallFolder", LeftoverKind.Folder, LeftoverConfidence.High);
            AssertItem(results, @"C:\Program Files\Acme\Studio", LeftoverKind.Folder, LeftoverConfidence.Medium);
            AssertItem(results, @"C:\Menu\Programs\Studio", LeftoverKind.Folder, LeftoverConfidence.High);
            AssertItem(results, @"C:\Menu\Programs\Studio\Studio.lnk", LeftoverKind.Shortcut, LeftoverConfidence.High);
            AssertItem(results, @"C:\Desktop\Studio.lnk", LeftoverKind.Shortcut, LeftoverConfidence.High);
            AssertItem(results, @"HKLM\SOFTWARE\Studio", LeftoverKind.RegistryKey, LeftoverConfidence.High);
            AssertItem(results, @"HKLM\SOFTWARE\WOW6432Node\Acme\Studio", LeftoverKind.RegistryKey, LeftoverConfidence.Medium);
            AssertItem(results, @"HKCU\SOFTWARE\Studio", LeftoverKind.RegistryKey, LeftoverConfidence.High);
            AssertItem(results, @"HKCU\" + InstalledProgramService.UninstallPath + @"\studio-entry",
                LeftoverKind.RegistryKey, LeftoverConfidence.High);
        }

        [Theory]
        [InlineData(@"C:\Program Files\")]
        [InlineData(@"C:\Program Files\Acme\..")]
        [InlineData(@"C:\Program Files Backup\Studio")]
        [InlineData(@"C:\Program Files\..\Elsewhere\Studio")]
        [InlineData(@"C:\Windows\Studio")]
        [InlineData(@"C:\Program Files\Studio")]
        [InlineData(@"C:\Program Files\Acme\Studio")]
        public void ExcludesRootsWindowsOutsidePathsAndReparsePoints(string location)
        {
            var files = new FileSystem();
            files.Add(@"C:\Program Files\Studio", FileAttributes.Directory | FileAttributes.ReparsePoint);
            files.Add(@"C:\Program Files\Acme", FileAttributes.Directory | FileAttributes.ReparsePoint);
            files.Add(@"C:\Program Files\Acme\Studio");
            files.Add(@"C:\Program Files Backup\Studio");
            files.Add(@"C:\Elsewhere\Studio");
            files.Add(@"C:\Windows\Studio");
            files.Add(@"C:\Menu\Programs\Studio\Studio.lnk", FileAttributes.Normal);
            files.Add(@"C:\Menu\Programs", FileAttributes.Directory | FileAttributes.ReparsePoint);
            files.Add(@"C:\Desktop\Studio.lnk", FileAttributes.ReparsePoint);
            var scanner = Scanner(files, new Registry());

            Assert.Empty(scanner.Scan(Program(location)));
            Assert.DoesNotContain(@"C:\Program Files\Acme", files.EnumeratedDirectories);
            Assert.DoesNotContain(@"C:\Menu\Programs", files.EnumeratedDirectories);
        }

        private static LeftoverScanner Scanner(FileSystem files, Registry registry) =>
            new LeftoverScanner(files, registry, @"C:\Windows", new[] { @"C:\Program Files", @"C:\Windows" },
                new[] { @"C:\Menu" }, new[] { @"C:\Desktop" }, is64Bit: true);

        private static InstalledProgram Program(string location) => new InstalledProgram
        {
            DisplayName = "Studio 2.0 (x64)",
            Publisher = "Acme, Inc.",
            InstallLocation = location,
            Hive = RegistryHive.CurrentUser,
            View = RegistryView.Registry64,
            SubKeyName = "studio-entry"
        };

        private static void AssertItem(Dictionary<string, UninstallLeftover> results, string path,
            LeftoverKind kind, LeftoverConfidence confidence)
        {
            Assert.Equal(kind, results[path].Kind);
            Assert.Equal(confidence, results[path].Confidence);
        }

        private sealed class FileSystem : ILeftoverFileSystem
        {
            private readonly Dictionary<string, FileAttributes> _entries =
                new Dictionary<string, FileAttributes>(StringComparer.OrdinalIgnoreCase);

            public List<string> EnumeratedDirectories { get; } = new List<string>();

            public void Add(string path, FileAttributes attributes = FileAttributes.Directory)
            {
                _entries[path] = attributes;
                for (var parent = Path.GetDirectoryName(path); parent != null && !_entries.ContainsKey(parent);
                    parent = Path.GetDirectoryName(parent))
                    _entries.Add(parent, FileAttributes.Directory);
            }

            public FileAttributes? GetAttributes(string path) =>
                _entries.TryGetValue(path, out var attributes) ? (FileAttributes?)attributes : null;

            public IEnumerable<string> GetDirectories(string path)
            {
                EnumeratedDirectories.Add(path);
                return Children(path, directories: true);
            }

            public IEnumerable<string> GetFiles(string path) => Children(path, directories: false);

            private IEnumerable<string> Children(string path, bool directories) => _entries
                .Where(entry => ((entry.Value & FileAttributes.Directory) != 0) == directories &&
                                string.Equals(Path.GetDirectoryName(entry.Key), path, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Key);
        }

        private sealed class Registry : ILeftoverRegistry
        {
            private readonly Dictionary<(RegistryHive, RegistryView), HashSet<string>> _keys =
                new Dictionary<(RegistryHive, RegistryView), HashSet<string>>();

            public void Add(RegistryHive hive, RegistryView view, string path)
            {
                if (!_keys.TryGetValue((hive, view), out var keys))
                    _keys.Add((hive, view), keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase));

                for (var key = path; key != null; key = Parent(key))
                    keys.Add(key);
            }

            public bool KeyExists(RegistryHive hive, RegistryView view, string path) =>
                _keys.TryGetValue((hive, view), out var keys) && keys.Contains(path);

            public IEnumerable<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string path) =>
                _keys.TryGetValue((hive, view), out var keys)
                    ? keys.Where(key => string.Equals(Parent(key), path, StringComparison.OrdinalIgnoreCase))
                        .Select(key => key.Substring(path.Length + 1))
                    : Enumerable.Empty<string>();

            private static string Parent(string key)
            {
                var separator = key.LastIndexOf('\\');
                return separator < 0 ? null : key.Substring(0, separator);
            }
        }
    }
}
