using System;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace OtimizaPC.Tweaks.Apps
{
    /// <summary>Pacote Appx/MSIX instalado para o usuário atual (saída do Get-AppxPackage).</summary>
    [DataContract]
    public sealed class AppPackage
    {
        [DataMember] public string Name { get; set; }

        [DataMember] public string PackageFullName { get; set; }

        [DataMember] public string PackageFamilyName { get; set; }

        [DataMember] public string Publisher { get; set; }

        [DataMember] public string Version { get; set; }

        [DataMember] public string SignatureKind { get; set; }

        [DataMember] public bool NonRemovable { get; set; }

        [DataMember] public string InstallLocation { get; set; }

        /// <summary>Caminho do logo definido pelo manifesto Appx.</summary>
        public string LogoPath { get; set; }

        /// <summary>Nome exibido lido do manifesto do pacote (preenchido pelo AppxService).</summary>
        public string ManifestDisplayName { get; set; }

        /// <summary>Nome do publicador extraído do certificado (CN=...); null quando é só o GUID da Store.</summary>
        public string PublisherName
        {
            get
            {
                if (string.IsNullOrEmpty(Publisher)) return null;
                var match = Regex.Match(Publisher, @"(?:^|,\s*)(?:CN|O)=(""[^""]+""|[^,]+)");
                var name = match.Success ? match.Groups[1].Value.Trim('"') : Publisher;
                return Regex.IsMatch(name, @"^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$") ? null : name;
            }
        }

        /// <summary>Nome do manifesto ou, na falta dele, derivado do identificador ("AD2F1837.HPSupportAssistant" → "HP Support Assistant").</summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ManifestDisplayName)) return ManifestDisplayName;
                var name = Name ?? string.Empty;
                var dot = name.LastIndexOf('.');
                var tail = dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
                return Regex.Replace(tail, "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
            }
        }

        public bool IsStore => string.Equals(SignatureKind, "Store", StringComparison.OrdinalIgnoreCase);
    }
}
