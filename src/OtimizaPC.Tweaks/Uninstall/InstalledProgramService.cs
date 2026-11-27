using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using Microsoft.Win32;

namespace OtimizaPC.Tweaks.Uninstall
{
    public sealed class InstalledProgramService
    {
        internal const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        private static readonly string[] ValueNames =
        {
            "DisplayName", "DisplayVersion", "Publisher", "InstallDate", "EstimatedSize",
            "InstallLocation", "DisplayIcon", "UninstallString", "QuietUninstallString",
            "SystemComponent", "ParentKeyName", "ReleaseType", "WindowsInstaller", "ProductCode"
        };

        public IReadOnlyList<InstalledProgram> ListInstalled()
        {
            var programs = new List<InstalledProgram>();
            if (Environment.Is64BitOperatingSystem)
                ReadPrograms(RegistryHive.LocalMachine, RegistryView.Registry64, programs);

            ReadPrograms(RegistryHive.LocalMachine, RegistryView.Registry32, programs);

            // O caminho em HKCU é compartilhado entre as visões nos sistemas suportados.
            ReadPrograms(RegistryHive.CurrentUser,
                Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32, programs);
            return Deduplicate(programs);
        }

        private static void ReadPrograms(RegistryHive hive, RegistryView view, List<InstalledProgram> programs)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, view))
                using (var uninstall = root.OpenSubKey(UninstallPath, writable: false))
                {
                    if (uninstall == null) return;

                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using (var key = uninstall.OpenSubKey(name, writable: false))
                            {
                                if (key == null) continue;

                                var values = ValueNames.ToDictionary(valueName => valueName, valueName => key.GetValue(valueName));
                                var program = Parse(values, hive, view, name);
                                if (program != null) programs.Add(program);
                            }
                        }
                        catch (Exception ex) when (IsRegistryAccessError(ex))
                        {
                            // Uma entrada inacessível não impede a leitura das demais.
                        }
                    }
                }
            }
            catch (Exception ex) when (IsRegistryAccessError(ex))
            {
                // Uma origem inacessível não impede a leitura das outras visões.
            }
        }

        private static bool IsRegistryAccessError(Exception ex) =>
            ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException;

        internal static InstalledProgram Parse(
            IReadOnlyDictionary<string, object> values, RegistryHive hive, RegistryView view, string subKeyName)
        {
            var name = ReadText(values, "DisplayName");
            var uninstallString = ReadText(values, "UninstallString");
            var releaseType = ReadText(values, "ReleaseType");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(uninstallString) ||
                ReadText(values, "SystemComponent") == "1" || !string.IsNullOrEmpty(ReadText(values, "ParentKeyName")) ||
                (releaseType != null && (releaseType.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         releaseType.IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0)))
                return null;

            return new InstalledProgram
            {
                DisplayName = name,
                DisplayVersion = ReadText(values, "DisplayVersion"),
                Publisher = ReadText(values, "Publisher"),
                InstallDate = DateTime.TryParseExact(ReadText(values, "InstallDate"), "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? (DateTime?)date : null,
                EstimatedSizeKb = long.TryParse(ReadText(values, "EstimatedSize"), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var size) ? (long?)size : null,
                InstallLocation = ReadText(values, "InstallLocation"),
                DisplayIcon = ReadText(values, "DisplayIcon"),
                UninstallString = uninstallString,
                QuietUninstallString = ReadText(values, "QuietUninstallString"),
                Hive = hive,
                View = view,
                SubKeyName = subKeyName,
                IsMsi = ReadText(values, "WindowsInstaller") == "1" ||
                        Guid.TryParse(subKeyName, out _) || Guid.TryParse(ReadText(values, "ProductCode"), out _)
            };
        }

        internal static IReadOnlyList<InstalledProgram> Deduplicate(IEnumerable<InstalledProgram> programs)
        {
            var seen = new HashSet<(string Name, string Version, string Publisher)>();
            return programs.Where(program => seen.Add((
                program.DisplayName.ToUpperInvariant(),
                program.DisplayVersion?.ToUpperInvariant() ?? string.Empty,
                program.Publisher?.ToUpperInvariant() ?? string.Empty))).ToList();
        }

        private static string ReadText(IReadOnlyDictionary<string, object> values, string name)
        {
            values.TryGetValue(name, out var value);
            if (value is string text) return text.Trim();
            return value is int || value is long ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }
    }
}
