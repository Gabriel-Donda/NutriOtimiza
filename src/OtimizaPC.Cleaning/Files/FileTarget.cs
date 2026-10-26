using System;
using System.IO;

namespace OtimizaPC.Cleaning.Files
{
    /// <summary>Pasta (e filtro) cujos arquivos podem ser removidos.</summary>
    public sealed class FileTarget
    {
        public FileTarget(string root, string pattern = "*", bool recursive = true, TimeSpan? minAge = null, bool removeEmptyFolders = true)
        {
            var expanded = Environment.ExpandEnvironmentVariables(root ?? string.Empty);
            // "C:\" não pode virar "C:": isso é um caminho relativo à unidade (diretório atual), não a raiz
            Root = IsDriveRoot(expanded) ? expanded : expanded.TrimEnd('\\');
            Pattern = pattern;
            Recursive = recursive;
            MinAge = minAge ?? TimeSpan.Zero;
            RemoveEmptyFolders = removeEmptyFolders && recursive;
        }

        private static bool IsDriveRoot(string path)
        {
            var trimmed = path.TrimEnd('\\');
            return trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':';
        }

        public string Root { get; }

        public string Pattern { get; }

        public bool Recursive { get; }

        /// <summary>Só remove arquivos sem modificação há pelo menos este tempo (protege instalações em andamento).</summary>
        public TimeSpan MinAge { get; }

        public bool RemoveEmptyFolders { get; }

        /// <summary>Alvo de um único arquivo com nome exato (ex.: C:\Windows\MEMORY.DMP).</summary>
        public bool IsSingleFile =>
            !Recursive &&
            !string.IsNullOrWhiteSpace(Pattern) &&
            Pattern.IndexOfAny(new[] { '*', '?' }) < 0 &&
            // Só aceita nome de arquivo simples: evita quebra de diretório ("..\arquivo.dmp")
            string.Equals(Pattern, Path.GetFileName(Pattern), StringComparison.Ordinal);

        public override string ToString() => $"{Root}\\{Pattern}";
    }
}
