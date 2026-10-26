using System.Collections.Generic;
using System.Threading;

namespace OtimizaPC.Cleaning
{
    public enum CleanupRisk
    {
        /// <summary>Pode ser removido sem efeito colateral perceptível.</summary>
        Safe,

        /// <summary>Seguro, mas com efeito colateral (ex.: recompilar shaders, perder dumps de diagnóstico).</summary>
        Moderate,
    }

    public static class CleanupGroups
    {
        public const string Windows = "Windows";
        public const string Browsers = "Navegadores";
        public const string Apps = "Aplicativos e jogos";
        public const string Privacy = "Privacidade";
    }

    /// <summary>Resultado da análise de um item (o que seria removido).</summary>
    public sealed class CleanupScan
    {
        public static readonly CleanupScan Empty = new CleanupScan();

        public long Bytes { get; set; }

        public int ItemCount { get; set; }

        /// <summary>False quando o tamanho só é conhecido após executar (ex.: DISM).</summary>
        public bool SizeKnown { get; set; } = true;

        public IReadOnlyCollection<string> Files { get; set; } = new string[0];

        /// <summary>Pastas raiz cujas subpastas vazias podem ser removidas após a limpeza.</summary>
        public IReadOnlyCollection<string> FolderRoots { get; set; } = new string[0];

        public int InaccessibleFolders { get; set; }

        public string Note { get; set; }
    }

    public sealed class CleanResult
    {
        public long FreedBytes { get; set; }

        public int Deleted { get; set; }

        /// <summary>Itens não removidos (em uso ou sem permissão).</summary>
        public int Skipped { get; set; }

        public string Note { get; set; }
    }

    /// <summary>Um item da lista de limpeza (ex.: "Cache do Chrome").</summary>
    public abstract class Cleaner
    {
        protected Cleaner(string id, string name, string description, string group,
            CleanupRisk risk = CleanupRisk.Safe, bool requiresAdmin = false, bool defaultSelected = true)
        {
            Id = id;
            Name = name;
            Description = description;
            Group = group;
            Risk = risk;
            RequiresAdmin = requiresAdmin;
            DefaultSelected = defaultSelected;
        }

        public string Id { get; }

        public string Name { get; }

        public string Description { get; }

        public string Group { get; }

        public CleanupRisk Risk { get; }

        public bool RequiresAdmin { get; }

        public bool DefaultSelected { get; }

        /// <summary>False quando o item não existe neste computador (ex.: navegador não instalado).</summary>
        public virtual bool IsAvailable => true;

        /// <summary>Motivo que impede a limpeza agora (ex.: navegador aberto); null quando pode limpar.</summary>
        public virtual string GetBlockedReason() => null;

        public abstract CleanupScan Scan(CancellationToken cancellationToken);

        public abstract CleanResult Clean(CleanupScan scan, CancellationToken cancellationToken);
    }
}
