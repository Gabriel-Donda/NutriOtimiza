using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using OtimizaPC.Core;

namespace OtimizaPC.Cleaning.Files
{
    /// <summary>Lista os arquivos que seriam removidos pelos alvos informados, sem apagar nada.</summary>
    public static class FileScanner
    {
        public static CleanupScan Scan(IEnumerable<FileTarget> targets, CancellationToken cancellationToken)
        {
            var fileTargets = new Dictionary<string, FileTarget>(StringComparer.OrdinalIgnoreCase);
            var roots = new List<string>();
            long bytes = 0;
            var inaccessible = 0;

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!SafePath.IsAllowed(target))
                {
                    Log.Warn("Alvo de limpeza recusado por segurança: " + target);
                    continue;
                }

                var nowUtc = DateTime.UtcNow;
                var root = Path.GetFullPath(target.Root);
                // Arquivo específico (ex.: C:\Windows\MEMORY.DMP): não enumera a pasta, só o arquivo exato
                if (target.IsSingleFile)
                {
                    try
                    {
                        if (!SafePath.TryGetContainedPath(root, Path.Combine(root, target.Pattern), out var path) ||
                            !SafePath.TryGetAttributesWithoutReparsePoints(root, path, out var attributes) ||
                            (attributes & FileAttributes.Directory) != 0)
                            continue;
                        AddFile(new FileInfo(path), target, nowUtc, fileTargets, ref bytes);
                    }
                    catch (FileNotFoundException) { }
                    catch (DirectoryNotFoundException) { }
                    catch (Exception ex) when (SafePath.IsFileSystemError(ex))
                    {
                        Log.Warn($"Alvo de limpeza sem acesso: {target}: {ex.Message}");
                    }
                    continue;
                }

                var rootInfo = new DirectoryInfo(root);
                if (!rootInfo.Exists) continue;
                if (SafePath.IsReparsePoint(rootInfo.Attributes))
                {
                    Log.Warn("Alvo de limpeza ignorado por ser um link simbólico: " + target);
                    continue;
                }

                if (target.RemoveEmptyFolders) roots.Add(root);
                var pending = new Stack<DirectoryInfo>();
                pending.Push(rootInfo);

                while (pending.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dir = pending.Pop();
                    try
                    {
                        if (!SafePath.TryGetAttributesWithoutReparsePoints(root, dir.FullName, out _)) continue;

                        foreach (var file in dir.EnumerateFiles(target.Pattern))
                        {
                            if (SafePath.IsReparsePoint(file.Attributes)) continue;
                            AddFile(file, target, nowUtc, fileTargets, ref bytes);
                        }

                        if (!target.Recursive) continue;
                        foreach (var sub in dir.EnumerateDirectories())
                        {
                            // Nunca seguir junctions/links simbólicos: podem apontar para fora da pasta alvo
                            if (!SafePath.IsReparsePoint(sub.Attributes))
                                pending.Push(sub);
                        }
                    }
                    catch (Exception ex) when (SafePath.IsFileSystemError(ex))
                    {
                        inaccessible++;
                    }
                }
            }

            return new CleanupScan
            {
                Bytes = bytes,
                ItemCount = fileTargets.Count,
                Files = fileTargets.Keys,
                FileTargets = fileTargets,
                FolderRoots = roots,
                InaccessibleFolders = inaccessible,
            };
        }

        private static void AddFile(FileInfo file, FileTarget target, DateTime nowUtc,
            Dictionary<string, FileTarget> fileTargets, ref long bytes)
        {
            if (target.MinAge > TimeSpan.Zero && !SafePath.MeetsMinAge(file.LastWriteTimeUtc, target.MinAge, nowUtc))
                return;

            if (fileTargets.TryGetValue(file.FullName, out var previous))
            {
                // Um dos alvos que aceitou o arquivo basta. Preserve o menor limite de idade.
                if (target.MinAge < previous.MinAge) fileTargets[file.FullName] = target;
                return;
            }

            bytes += file.Length;
            fileTargets.Add(file.FullName, target);
        }
    }
}
