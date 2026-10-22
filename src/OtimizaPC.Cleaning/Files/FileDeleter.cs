using System;
using System.IO;
using System.Threading;

namespace OtimizaPC.Cleaning.Files
{
    /// <summary>Remove os arquivos listados na análise. Arquivos em uso são ignorados, nunca forçados.</summary>
    public static class FileDeleter
    {
        public static CleanResult Delete(CleanupScan scan, CancellationToken cancellationToken)
        {
            var result = new CleanResult();

            foreach (var path in scan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var file = new FileInfo(path);
                    if (!file.Exists) continue;

                    var length = file.Length;
                    if ((file.Attributes & FileAttributes.ReadOnly) != 0)
                        file.Attributes &= ~FileAttributes.ReadOnly;
                    file.Delete();

                    result.FreedBytes += length;
                    result.Deleted++;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    result.Skipped++;
                }
            }

            foreach (var root in scan.FolderRoots)
                RemoveEmptySubfolders(new DirectoryInfo(root));

            return result;
        }

        /// <summary>Remove subpastas vazias (a pasta raiz é mantida). Não entra em links simbólicos.</summary>
        internal static void RemoveEmptySubfolders(DirectoryInfo root)
        {
            DirectoryInfo[] subs;
            try
            {
                if (!root.Exists) return;
                subs = root.GetDirectories();
            }
            catch
            {
                return;
            }

            foreach (var sub in subs)
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                RemoveEmptySubfolders(sub);
                try
                {
                    // Delete sem recursão: só funciona se a pasta estiver realmente vazia
                    sub.Delete(false);
                }
                catch
                {
                    // Pasta ainda tem conteúdo, está em uso ou sem permissão
                }
            }
        }
    }
}
