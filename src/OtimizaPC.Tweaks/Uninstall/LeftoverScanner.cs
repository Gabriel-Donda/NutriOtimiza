using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OtimizaPC.Tweaks.Uninstall
{
    public enum LeftoverKind { Folder, RegistryKey, Shortcut }

    public enum LeftoverConfidence { High, Medium }

    public sealed class UninstallLeftover
    {
        internal UninstallLeftover(LeftoverKind kind, string path, LeftoverConfidence confidence,
            RegistryHive hive = RegistryHive.CurrentUser, RegistryView view = RegistryView.Default, string registryPath = null)
        {
            Kind = kind;
            Path = path;
            Confidence = confidence;
            Hive = hive;
            View = view;
            RegistryPath = registryPath;
        }

        public LeftoverKind Kind { get; }
        public string Path { get; }
        public LeftoverConfidence Confidence { get; }
        internal RegistryHive Hive { get; }
        internal RegistryView View { get; }
        internal string RegistryPath { get; }
    }

    internal interface ILeftoverFileSystem
    {
        FileAttributes? GetAttributes(string path);
        IEnumerable<string> GetDirectories(string path);
        IEnumerable<string> GetFiles(string path);
    }

    internal interface ILeftoverRegistry
    {
        bool KeyExists(RegistryHive hive, RegistryView view, string path);
        IEnumerable<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string path);
    }

    public sealed class LeftoverScanner
    {
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
        private static readonly HashSet<string> GenericTerms = new HashSet<string>(Names)
        {
            "Microsoft", "Windows", "Intel", "AMD", "NVIDIA", "Google", "Common Files",
            "Update", "Updates", "Software", "Program", "Programs", "Program Files",
            "Program Files (x86)", "ProgramData", "AppData", "System", "System32",
            "Driver", "Drivers", "Application", "Applications", "Apps", "Common", "Shared",
            "Default", "Desktop", "Start Menu", "Temp", "Packages", "Installer", "Setup",
            "Uninstall", "WOW6432Node", "Classes", "Policies", "RegisteredApplications", "Clients", "Tools", "Utilities", "Windows Installer", "Program Files (x64)",
            "System Files", "User Data", "Public", "Data", "Settings", "Config", "Cache"
        };

        private const string Version = @"(?:(?:version|versão|v)\s*)?\d+(?:[._-]\d+)*";
        private const string Architecture = @"(?:x86|x64|arm64|amd64|(?:32|64)[ -]?bits?)";
        private const string QualifierSeparator = @"(?:\s*[,;/-]\s*|\s+)";
        private static readonly Regex DisplaySuffix = new Regex(
            @"(?:\s*\(\s*(?:" + Version + "(?:" + QualifierSeparator + Architecture + ")?|" +
            Architecture + "(?:" + QualifierSeparator + Version + @")?)\s*\)|\s+" + Version + @")\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex PublisherSuffix = new Regex(
            @"[\s,]+(?:Inc|Ltd|LLC|GmbH|Corporation|Co|S\.?A)\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly ILeftoverFileSystem _files;
        private readonly ILeftoverRegistry _registry;
        private readonly string _windows;
        private readonly string[] _dataRoots;
        private readonly string[] _startMenus;
        private readonly string[] _desktops;
        private readonly HashSet<string> _roots;
        private readonly bool _is64Bit;

        public LeftoverScanner() : this(new LeftoverFileSystem(), new LeftoverRegistry(),
            Folder(Environment.SpecialFolder.Windows),
            new[]
            {
                Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                    ? Environment.GetEnvironmentVariable("ProgramW6432") : null,
                Folder(Environment.SpecialFolder.ProgramFiles),
                Folder(Environment.SpecialFolder.ProgramFilesX86), Folder(Environment.SpecialFolder.CommonApplicationData),
                Folder(Environment.SpecialFolder.ApplicationData), Folder(Environment.SpecialFolder.LocalApplicationData)
            },
            new[] { Folder(Environment.SpecialFolder.CommonStartMenu), Folder(Environment.SpecialFolder.StartMenu) },
            new[] { Folder(Environment.SpecialFolder.CommonDesktopDirectory), Folder(Environment.SpecialFolder.DesktopDirectory) },
            Environment.Is64BitOperatingSystem)
        {
        }

        internal LeftoverScanner(ILeftoverFileSystem files, ILeftoverRegistry registry, string windows,
            IEnumerable<string> dataRoots, IEnumerable<string> startMenus, IEnumerable<string> desktops, bool is64Bit)
        {
            _files = files;
            _registry = registry;
            _windows = NormalizePath(windows);
            _dataRoots = NormalizeRoots(dataRoots);
            _startMenus = NormalizeRoots(startMenus);
            _desktops = NormalizeRoots(desktops);
            _roots = new HashSet<string>(_dataRoots.Concat(_startMenus).Concat(_desktops), Names);
            _is64Bit = is64Bit;
        }

        public bool UninstallKeyExists(InstalledProgram program)
        {
            if (!IsRegistryName(program.SubKeyName)) return true;

            try
            {
                return _registry.KeyExists(program.Hive, program.View, UninstallPath(program));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                // Sem confirmação de que a chave sumiu, bloqueia a limpeza.
                OtimizaPC.Core.Log.Error("Não foi possível verificar a chave Uninstall", ex);
                return true;
            }
        }

        internal bool IsAllowedFilePath(string path, LeftoverKind kind)
        {
            if (!Names.Equals(path, NormalizePath(path)) || path == null || _windows == null ||
                Names.Equals(path, _windows) || IsBelow(path, _windows) || _roots.Contains(path))
                return false;

            if (kind == LeftoverKind.Folder)
                return _dataRoots.Concat(_startMenus).Any(root => IsBelow(path, root));
            return kind == LeftoverKind.Shortcut && Names.Equals(Path.GetExtension(path), ".lnk") &&
                _startMenus.Concat(_desktops).Any(root => IsBelow(path, root));
        }

        internal bool IsSafeFile(string path, LeftoverKind kind)
        {
            if (!IsAllowedFilePath(path, kind)) return false;
            if (kind == LeftoverKind.Folder)
                return IsSafeDirectory(path);

            if (!IsSafeDirectory(Path.GetDirectoryName(path))) return false;
            var attributes = _files.GetAttributes(path);
            return attributes.HasValue &&
                (attributes.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }

        public IReadOnlyList<UninstallLeftover> Scan(InstalledProgram program)
        {
            var product = CleanDisplayName(program.DisplayName);
            var publisher = CleanPublisher(program.Publisher);
            var results = new List<UninstallLeftover>();

            var installLocation = program.InstallLocation?.Trim();
            if (!string.IsNullOrEmpty(installLocation) && installLocation.Length >= 2 &&
                installLocation[0] == '"' && installLocation[installLocation.Length - 1] == '"')
                installLocation = installLocation.Substring(1, installLocation.Length - 2);

            var location = NormalizePath(Environment.ExpandEnvironmentVariables(installLocation ?? string.Empty));
            if (location != null && _dataRoots.Any(root => IsBelow(location, root)) && IsSafeFolder(location))
                results.Add(new UninstallLeftover(LeftoverKind.Folder, location, LeftoverConfidence.High));

            if (product != null)
            {
                foreach (var root in _dataRoots)
                {
                    foreach (var folder in SafeDirectories(root))
                    {
                        var name = Path.GetFileName(folder);
                        if (Names.Equals(name, product) && !_roots.Contains(folder))
                            results.Add(new UninstallLeftover(LeftoverKind.Folder, folder, LeftoverConfidence.High));
                        if (Names.Equals(name, publisher))
                        {
                            foreach (var child in SafeDirectories(folder))
                            {
                                if (Names.Equals(Path.GetFileName(child), product) && !_roots.Contains(child))
                                    results.Add(new UninstallLeftover(LeftoverKind.Folder, child, LeftoverConfidence.Medium));
                            }
                        }
                    }
                }

                if (_is64Bit)
                    ScanRegistry(RegistryHive.LocalMachine, RegistryView.Registry64, product, publisher, results);
                ScanRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, product, publisher, results);
                ScanRegistry(RegistryHive.CurrentUser, _is64Bit ? RegistryView.Registry64 : RegistryView.Registry32,
                    product, publisher, results);

                foreach (var root in _startMenus)
                    ScanShortcuts(root, product, includeSubdirectories: true, results);
                foreach (var root in _desktops)
                    ScanShortcuts(root, product, includeSubdirectories: false, results);
            }

            if (IsRegistryName(program.SubKeyName) && UninstallKeyExists(program))
                results.Add(RegistryLeftover(program.Hive, program.View, UninstallPath(program), LeftoverConfidence.High));

            // TODO: incluir tarefas agendadas em uma etapa futura.
            return results.GroupBy(item => item.Path, Names)
                .Select(group => group.OrderBy(item => item.Confidence).First()).ToList();
        }

        internal static string CleanDisplayName(string name) => CleanTerm(name, DisplaySuffix);

        internal static string CleanPublisher(string publisher) => CleanTerm(publisher, PublisherSuffix);

        private static string CleanTerm(string value, Regex suffix)
        {
            var term = (value ?? string.Empty).Trim();
            while (suffix.IsMatch(term))
                term = suffix.Replace(term, string.Empty).Trim();

            return term.Length >= 4 && !GenericTerms.Contains(term) ? term : null;
        }

        private void ScanRegistry(RegistryHive hive, RegistryView view, string product, string publisher,
            List<UninstallLeftover> results)
        {
            const string root = "SOFTWARE";
            var names = new HashSet<string>(_registry.GetSubKeyNames(hive, view, root), Names);
            if (IsRegistryName(product) && names.Contains(product))
                results.Add(RegistryLeftover(hive, view, root + "\\" + product, LeftoverConfidence.High));

            if (!IsRegistryName(product) || !IsRegistryName(publisher) || !names.Contains(publisher)) return;

            var publisherPath = root + "\\" + publisher;
            if (_registry.GetSubKeyNames(hive, view, publisherPath).Contains(product, Names))
                results.Add(RegistryLeftover(hive, view, publisherPath + "\\" + product, LeftoverConfidence.Medium));
        }

        private void ScanShortcuts(string root, string product, bool includeSubdirectories, List<UninstallLeftover> results)
        {
            if (!IsSafeDirectory(root)) return;

            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var folder = pending.Pop();
                foreach (var candidate in _files.GetFiles(folder))
                {
                    var path = NormalizePath(candidate);
                    if (path == null || !Names.Equals(Path.GetDirectoryName(path), folder) ||
                        !Names.Equals(Path.GetExtension(path), ".lnk") ||
                        !Names.Equals(Path.GetFileNameWithoutExtension(path), product)) continue;

                    var attributes = _files.GetAttributes(path);
                    if (attributes.HasValue && (attributes.Value & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0)
                        results.Add(new UninstallLeftover(LeftoverKind.Shortcut, path, LeftoverConfidence.High));
                }

                if (!includeSubdirectories) continue;

                foreach (var child in SafeDirectories(folder))
                {
                    if (Names.Equals(Path.GetFileName(child), product) && !_roots.Contains(child))
                        results.Add(new UninstallLeftover(LeftoverKind.Folder, child, LeftoverConfidence.High));
                    pending.Push(child);
                }
            }
        }

        private IEnumerable<string> SafeDirectories(string parent)
        {
            if (!IsSafeDirectory(parent)) yield break;

            foreach (var candidate in _files.GetDirectories(parent))
            {
                var path = NormalizePath(candidate);
                if (path != null && Names.Equals(Path.GetDirectoryName(path), parent) && IsSafeDirectory(path))
                    yield return path;
            }
        }

        private bool IsSafeFolder(string path) => !_roots.Contains(path) && IsSafeDirectory(path);

        private bool IsSafeDirectory(string path)
        {
            if (_windows == null || Names.Equals(path, _windows) || IsBelow(path, _windows)) return false;

            // Verifica de cima para baixo para não consultar caminhos através de uma junção.
            var ancestors = new Stack<string>();
            for (var current = path; current != null; current = Path.GetDirectoryName(current))
                ancestors.Push(current);

            foreach (var directory in ancestors)
            {
                var attributes = _files.GetAttributes(directory);
                if (!attributes.HasValue ||
                    (attributes.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                    return false;
            }
            return true;
        }

        private static bool IsBelow(string path, string root) =>
            path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) &&
            !Names.Equals(path, root);

        private static string[] NormalizeRoots(IEnumerable<string> paths) =>
            paths.Select(NormalizePath).Where(path => path != null).Distinct(Names).ToArray();

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            path = path.Trim().Replace('/', '\\');
            // Aceita caminhos absolutos de unidades e compartilhamentos, sem dispositivos ou fluxos alternativos.
            var drive = path.Length >= 3 && path[1] == ':' && path[2] == '\\' && path.IndexOf(':', 2) < 0;
            var share = path.StartsWith(@"\\", StringComparison.Ordinal) &&
                        !path.StartsWith(@"\\?\", StringComparison.Ordinal) &&
                        !path.StartsWith(@"\\.\", StringComparison.Ordinal) && path.IndexOf(':') < 0;
            if (!drive && !share) return null;

            try
            {
                var fullPath = Path.GetFullPath(path);
                return Names.Equals(fullPath, Path.GetPathRoot(fullPath)) ? fullPath : fullPath.TrimEnd('\\');
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException ||
                                       ex is IOException || ex is SecurityException)
            {
                return null;
            }
        }

        private static string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);

        private static bool IsRegistryName(string name) => !string.IsNullOrEmpty(name) && name.IndexOf('\\') < 0;

        private static string UninstallPath(InstalledProgram program) =>
            InstalledProgramService.UninstallPath + "\\" + program.SubKeyName;

        private UninstallLeftover RegistryLeftover(RegistryHive hive, RegistryView view, string path,
            LeftoverConfidence confidence) =>
            new UninstallLeftover(LeftoverKind.RegistryKey, RegistryDisplayPath(hive, view, path),
                confidence, hive, view, path);

        private string RegistryDisplayPath(RegistryHive hive, RegistryView view, string path)
        {
            if (hive == RegistryHive.CurrentUser) return @"HKCU\" + path;
            if (_is64Bit && view == RegistryView.Registry32)
                path = path.Insert("SOFTWARE".Length, @"\WOW6432Node");
            return @"HKLM\" + path;
        }
    }
}
