using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using OtimizaPC.Core;

namespace OtimizaPC.Tweaks.Apps
{
    public enum ReinstallOutcome
    {
        /// <summary>Reinstalado direto dos arquivos que continuam no sistema.</summary>
        Reinstalled,

        /// <summary>Arquivos não existem mais: a página do app na Microsoft Store foi aberta.</summary>
        OpenedStore,
    }

    [DataContract]
    public sealed class RemovedApp
    {
        [DataMember] public string Name { get; set; }

        [DataMember] public string DisplayName { get; set; }

        [DataMember] public string PackageFamilyName { get; set; }

        [DataMember] public string InstallLocation { get; set; }

        [DataMember] public DateTime RemovedAt { get; set; }
    }

    /// <summary>Lista, remove e reinstala apps da Store (Appx/MSIX) via Windows PowerShell.</summary>
    public sealed class AppxService
    {
        private readonly string _removedFile;

        public AppxService(string removedFile = null)
        {
            _removedFile = removedFile ?? Path.Combine(AppInfo.DataDirectory, "removed-apps.json");
        }

        public IReadOnlyList<AppPackage> ListInstalled()
        {
            const string script =
                "$list = Get-AppxPackage | Where-Object { -not $_.IsFramework } | Select-Object Name, PackageFullName, PackageFamilyName, Publisher, " +
                "@{n='Version';e={[string]$_.Version}}, @{n='SignatureKind';e={[string]$_.SignatureKind}}, " +
                "@{n='NonRemovable';e={[bool]$_.NonRemovable}}, InstallLocation; " +
                "ConvertTo-Json -Compress -Depth 2 -InputObject @($list)";

            var result = PowerShellRunner.Run(script);
            if (!result.Success)
                throw new InvalidOperationException("Não foi possível listar os apps: " + (result.Error ?? "código " + result.ExitCode));

            var packages = ParsePackages(result.Output);
            foreach (var package in packages)
                package.ManifestDisplayName = ReadManifestDisplayName(package);
            return packages;
        }

        /// <summary>Lê o DisplayName do AppxManifest.xml, resolvendo nomes traduzidos (ms-resource:).</summary>
        private static string ReadManifestDisplayName(AppPackage package)
        {
            try
            {
                if (string.IsNullOrEmpty(package.InstallLocation)) return null;
                var manifest = Path.Combine(package.InstallLocation, "AppxManifest.xml");
                if (!File.Exists(manifest)) return null;

                var doc = System.Xml.Linq.XDocument.Load(manifest);
                var value = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Properties")?
                    .Elements().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value?.Trim();
                if (string.IsNullOrEmpty(value)) return null;
                if (!value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return value;

                var key = value.Substring("ms-resource:".Length);
                var candidates = key.StartsWith("//", StringComparison.Ordinal)
                    ? new[] { value }
                    : new[] { $"ms-resource://{package.Name}/Resources/{key}", $"ms-resource://{package.Name}/{key}" };
                foreach (var resource in candidates)
                {
                    var resolved = ResolveIndirectString($"@{{{package.PackageFullName}? {resource}}}");
                    if (!string.IsNullOrEmpty(resolved)) return resolved;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Não foi possível ler o nome de {package.Name}: {ex.Message}");
            }
            return null;
        }

        private static string ResolveIndirectString(string source)
        {
            var buffer = new StringBuilder(512);
            return SHLoadIndirectString(source, buffer, buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString() : null;
        }

        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SHLoadIndirectString(string source, StringBuilder output, int outputSize, IntPtr reserved);

        internal static IReadOnlyList<AppPackage> ParsePackages(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new AppPackage[0];
            var text = json.Trim();
            // Garantia extra: um único objeto vira lista
            if (text.StartsWith("{", StringComparison.Ordinal)) text = "[" + text + "]";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                return (List<AppPackage>)new DataContractJsonSerializer(typeof(List<AppPackage>)).ReadObject(stream);
        }

        /// <summary>Remove o app do usuário atual; opcionalmente impede que seja instalado para novos usuários.</summary>
        public void Remove(AppPackage package, string displayName, bool alsoForNewUsers)
        {
            var script = $"Remove-AppxPackage -Package {PowerShellRunner.Quote(package.PackageFullName)}";
            var result = PowerShellRunner.Run(script);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "código " + result.ExitCode);

            if (alsoForNewUsers && AppInfo.IsAdministrator)
            {
                var deprovision =
                    $"Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -eq {PowerShellRunner.Quote(package.Name)} }} | " +
                    "ForEach-Object { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName | Out-Null }";
                var provisioned = PowerShellRunner.Run(deprovision);
                if (!provisioned.Success)
                    Log.Warn($"Removido do usuário, mas não do provisionamento ({package.Name}): {provisioned.Error}");
            }

            var removed = LoadRemoved();
            removed.RemoveAll(r => r.Name == package.Name);
            removed.Add(new RemovedApp
            {
                Name = package.Name,
                DisplayName = displayName,
                PackageFamilyName = package.PackageFamilyName,
                InstallLocation = package.InstallLocation,
                RemovedAt = DateTime.Now,
            });
            SaveRemoved(removed);
            History.Record("Apps", $"Removido: {displayName} ({package.Name})" + (alsoForNewUsers ? " — também para novos usuários" : string.Empty));
        }

        /// <summary>
        /// Tenta registrar de novo a partir dos arquivos que ficam no sistema (o caso comum quando só o usuário atual removeu);
        /// se não existirem mais, abre a página do app na Microsoft Store.
        /// </summary>
        public ReinstallOutcome Reinstall(RemovedApp app)
        {
            var manifest = string.IsNullOrEmpty(app.InstallLocation) ? null : Path.Combine(app.InstallLocation, "AppxManifest.xml");
            if (manifest != null && File.Exists(manifest))
            {
                var result = PowerShellRunner.Run($"Add-AppxPackage -DisableDevelopmentMode -Register {PowerShellRunner.Quote(manifest)}");
                if (result.Success)
                {
                    ForgetRemoved(app.Name);
                    History.Record("Apps", $"Reinstalado: {app.DisplayName} ({app.Name})");
                    return ReinstallOutcome.Reinstalled;
                }
                Log.Warn($"Falha ao registrar {app.Name} de novo: {result.Error}");
            }

            Process.Start("ms-windows-store://pdp/?PFN=" + Uri.EscapeDataString(app.PackageFamilyName ?? app.Name));
            return ReinstallOutcome.OpenedStore;
        }

        public List<RemovedApp> LoadRemoved()
        {
            try
            {
                if (!File.Exists(_removedFile)) return new List<RemovedApp>();
                using (var stream = File.OpenRead(_removedFile))
                    return (List<RemovedApp>)new DataContractJsonSerializer(typeof(List<RemovedApp>)).ReadObject(stream);
            }
            catch (Exception ex)
            {
                Log.Warn("Lista de apps removidos ilegível: " + ex.Message);
                return new List<RemovedApp>();
            }
        }

        /// <summary>Tira da lista os apps que voltaram a estar instalados (ex.: reinstalados pela Store).</summary>
        public void SyncRemoved(IEnumerable<AppPackage> installed)
        {
            var names = new HashSet<string>(installed.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var removed = LoadRemoved();
            if (removed.RemoveAll(r => names.Contains(r.Name)) > 0) SaveRemoved(removed);
        }

        private void ForgetRemoved(string name)
        {
            var removed = LoadRemoved();
            if (removed.RemoveAll(r => r.Name == name) > 0) SaveRemoved(removed);
        }

        internal void SaveRemoved(List<RemovedApp> removed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_removedFile));
            using (var stream = File.Create(_removedFile))
                new DataContractJsonSerializer(typeof(List<RemovedApp>)).WriteObject(stream, removed);
        }
    }
}
