using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OtimizaPC.Cleaning.Files;

namespace OtimizaPC.Cleaning.Cleaners
{
    /// <summary>
    /// Cache de um aplicativo (navegador, Discord etc.). Só limpa com o aplicativo fechado,
    /// para não corromper o cache em uso.
    /// </summary>
    public sealed class AppCacheCleaner : FileCleaner
    {
        private readonly string _appName;
        private readonly string[] _processNames;

        public AppCacheCleaner(string id, string appName, string description, string group, IEnumerable<FileTarget> targets,
            string[] processNames, CleanupRisk risk = CleanupRisk.Safe, bool defaultSelected = true)
            : base(id, appName, description, group, targets, risk, requiresAdmin: false, defaultSelected: defaultSelected)
        {
            _appName = appName;
            _processNames = processNames;
        }

        public override string GetBlockedReason()
        {
            var running = _processNames.Any(name =>
            {
                var processes = Process.GetProcessesByName(name);
                foreach (var p in processes) p.Dispose();
                return processes.Length > 0;
            });
            return running ? $"Feche o {_appName} para limpar" : null;
        }

        /// <summary>Alvos de cache de um navegador baseado em Chromium, para todos os perfis.</summary>
        public static IEnumerable<FileTarget> ChromiumTargets(string userDataDir)
        {
            var dir = Environment.ExpandEnvironmentVariables(userDataDir);
            var targets = new List<FileTarget>
            {
                new FileTarget(System.IO.Path.Combine(dir, "ShaderCache")),
                new FileTarget(System.IO.Path.Combine(dir, "GrShaderCache")),
                new FileTarget(System.IO.Path.Combine(dir, "GraphiteDawnCache")),
            };

            foreach (var profile in ChromiumProfiles(dir))
            {
                targets.Add(new FileTarget(System.IO.Path.Combine(profile, "Cache")));
                targets.Add(new FileTarget(System.IO.Path.Combine(profile, "Code Cache")));
                targets.Add(new FileTarget(System.IO.Path.Combine(profile, "GPUCache")));
                targets.Add(new FileTarget(System.IO.Path.Combine(profile, "DawnGraphiteCache")));
                targets.Add(new FileTarget(System.IO.Path.Combine(profile, "DawnWebGPUCache")));
            }
            return targets;
        }

        private static IEnumerable<string> ChromiumProfiles(string userDataDir)
        {
            if (!System.IO.Directory.Exists(userDataDir)) return Enumerable.Empty<string>();
            try
            {
                return System.IO.Directory.GetDirectories(userDataDir)
                    .Where(d =>
                    {
                        var name = System.IO.Path.GetFileName(d);
                        return name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal);
                    })
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>Alvos de cache do Firefox, para todos os perfis.</summary>
        public static IEnumerable<FileTarget> FirefoxTargets()
        {
            var profiles = Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Mozilla\Firefox\Profiles");
            if (!System.IO.Directory.Exists(profiles)) return Enumerable.Empty<FileTarget>();
            try
            {
                return System.IO.Directory.GetDirectories(profiles)
                    .SelectMany(p => new[]
                    {
                        new FileTarget(System.IO.Path.Combine(p, "cache2")),
                        new FileTarget(System.IO.Path.Combine(p, "startupCache")),
                    })
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<FileTarget>();
            }
        }
    }
}
