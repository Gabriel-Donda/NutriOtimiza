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
                    if (string.IsNullOrEmpty(path) || !scan.FileTargets.TryGetValue(path, out var target) ||
                        !SafePath.TryGetContainedPath(target.Root, path, out var fullPath) ||
                        !TryGetFile(target.Root, fullPath, target.MinAge, out var file))
                    {
                        result.Skipped++;
                        continue;
                    }

                    var length = file.Length;
                    if ((file.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        file.Attributes &= ~FileAttributes.ReadOnly;
                        if (!TryGetFile(target.Root, fullPath, target.MinAge, out file))
                        {
                            result.Skipped++;
                            continue;
                        }
                    }
                    file.Delete();

                    result.FreedBytes += length;
                    result.Deleted++;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (Exception ex) when (SafePath.IsFileSystemError(ex))
                {
                    result.Skipped++;
                }
            }

            foreach (var root in scan.FolderRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SafePath.IsAllowedRoot(root))
                {
                    result.Skipped++;
                    continue;
                }

                var fullRoot = Path.GetFullPath(root);
                RemoveEmptySubfolders(fullRoot, fullRoot, result, cancellationToken);
            }

            return result;
        }

        private static bool TryGetFile(string root, string path, TimeSpan minAge, out FileInfo file)
        {
            file = null;
            if (!SafePath.TryGetAttributesWithoutReparsePoints(root, path, out var attributes) ||
                (attributes & FileAttributes.Directory) != 0)
                return false;

            file = new FileInfo(path);
            return minAge <= TimeSpan.Zero || SafePath.MeetsMinAge(file.LastWriteTimeUtc, minAge, DateTime.UtcNow);
        }

        private static bool IsRegularDirectory(string root, string path) =>
            SafePath.TryGetAttributesWithoutReparsePoints(root, path, out var attributes) &&
            (attributes & FileAttributes.Directory) != 0;

        /// <summary>Remove subpastas vazias e mantém a pasta raiz.</summary>
        private static void RemoveEmptySubfolders(string root, string directory, CleanResult result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isRoot = string.Equals(root, directory, StringComparison.OrdinalIgnoreCase);
            string[] subs;
            try
            {
                if (!IsRegularDirectory(root, directory))
                {
                    result.Skipped++;
                    return;
                }
                subs = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (SafePath.IsFileSystemError(ex))
            {
                result.Skipped++;
                return;
            }

            foreach (var sub in subs)
                RemoveEmptySubfolders(root, sub, result, cancellationToken);

            if (isRoot) return;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!IsRegularDirectory(root, directory))
                {
                    result.Skipped++;
                    return;
                }
            }
            catch (Exception ex) when (SafePath.IsFileSystemError(ex))
            {
                result.Skipped++;
                return;
            }

            try
            {
                // Sem recursão: a exclusão só funciona se a pasta estiver vazia.
                Directory.Delete(directory, false);
            }
            catch (Exception ex) when (SafePath.IsFileSystemError(ex))
            {
                // Pasta ainda tem conteúdo, está em uso ou sem permissão.
            }
        }
    }
}
