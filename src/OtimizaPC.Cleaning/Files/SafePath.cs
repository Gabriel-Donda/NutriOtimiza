using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OtimizaPC.Cleaning.Files
{
    /// <summary>
    /// Última barreira contra apagar o lugar errado: recusa varrer raízes de unidade,
    /// pastas do sistema e pastas pessoais do usuário, mesmo que um alvo seja mal configurado.
    /// </summary>
    public static class SafePath
    {
        private static readonly Lazy<HashSet<string>> Protected = new Lazy<HashSet<string>>(BuildProtectedList);

        public static bool IsAllowed(FileTarget target)
        {
            if (string.IsNullOrWhiteSpace(target?.Root)) return false;

            string full;
            try
            {
                full = Normalize(target.Root);
            }
            catch
            {
                return false;
            }

            // Caminhos de rede ou relativos nunca são alvo de limpeza
            if (full.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathRooted(full)) return false;

            // Unidade inteira (C:\) nunca
            if (full.Length <= 3) return false;

            // Pastas protegidas só podem ser alvo de um arquivo específico (ex.: MEMORY.DMP no Windows)
            if (Protected.Value.Contains(full)) return target.IsSingleFile;

            return true;
        }

        private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\');

        private static HashSet<string> BuildProtectedList()
        {
            var folders = new List<string>();
            foreach (Environment.SpecialFolder folder in Enum.GetValues(typeof(Environment.SpecialFolder)))
            {
                try
                {
                    var path = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrEmpty(path)) folders.Add(path);
                }
                catch
                {
                    // Pasta especial inexistente nesta versão do Windows
                }
            }

            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
            folders.Add(Path.Combine(systemDrive, "Users"));
            folders.Add(Path.Combine(systemDrive, "Windows", "System32"));
            folders.Add(Path.Combine(systemDrive, "Windows", "SysWOW64"));
            folders.Add(Path.Combine(systemDrive, "Windows", "WinSxS"));
            folders.Add(Path.Combine(systemDrive, "Recovery"));
            folders.Add(Path.Combine(systemDrive, "Boot"));

            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            folders.Add(downloads);

            return new HashSet<string>(
                folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(Normalize),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
