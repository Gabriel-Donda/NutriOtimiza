using System;

namespace OtimizaPC.Cleaning.Files
{
    /// <summary>Pasta (e filtro) cujos arquivos podem ser removidos.</summary>
    public sealed class FileTarget
    {
        public FileTarget(string root, string pattern = "*", bool recursive = true, TimeSpan? minAge = null, bool removeEmptyFolders = true)
        {
            Root = Environment.ExpandEnvironmentVariables(root ?? string.Empty).TrimEnd('\\');
            Pattern = pattern;
            Recursive = recursive;
            MinAge = minAge ?? TimeSpan.Zero;
            RemoveEmptyFolders = removeEmptyFolders && recursive;
        }

        public string Root { get; }

        public string Pattern { get; }

        public bool Recursive { get; }

        /// <summary>Só remove arquivos sem modificação há pelo menos este tempo (protege instalações em andamento).</summary>
        public TimeSpan MinAge { get; }

        public bool RemoveEmptyFolders { get; }

        /// <summary>Alvo de um único arquivo com nome exato (ex.: C:\Windows\MEMORY.DMP).</summary>
        public bool IsSingleFile => !Recursive && Pattern.IndexOfAny(new[] { '*', '?' }) < 0;

        public override string ToString() => $"{Root}\\{Pattern}";
    }
}
