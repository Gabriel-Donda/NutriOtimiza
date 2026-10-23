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
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

                // Arquivo específico (ex.: C:\Windows\MEMORY.DMP): não enumera a pasta, só o arquivo exato
                if (target.IsSingleFile)
                {
                    try
                    {
                        var file = new FileInfo(Path.Combine(target.Root, target.Pattern));
                        if (!file.Exists) continue;
                        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            Log.Warn("Alvo de limpeza ignorado por ser um link simbólico: " + target);
                            continue;
                        }
                        if (target.MinAge > TimeSpan.Zero && file.LastWriteTimeUtc > DateTime.UtcNow - target.MinAge) continue;
                        if (files.Add(file.FullName)) bytes += file.Length;
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
                    {
                        Log.Warn($"Alvo de limpeza sem acesso: {target}: {ex.Message}");
                    }
                    continue;
                }

                var rootInfo = new DirectoryInfo(target.Root);
                if (!rootInfo.Exists) continue;
                if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Log.Warn("Alvo de limpeza ignorado por ser um link simbólico: " + target);
                    continue;
                }

                if (target.RemoveEmptyFolders) roots.Add(target.Root);
                var cutoff = DateTime.UtcNow - target.MinAge;
                var pending = new Stack<DirectoryInfo>();
                pending.Push(rootInfo);

                while (pending.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dir = pending.Pop();
                    try
                    {
                        foreach (var file in dir.EnumerateFiles(target.Pattern))
                        {
                            if (target.MinAge > TimeSpan.Zero && file.LastWriteTimeUtc > cutoff) continue;
                            if (files.Add(file.FullName)) bytes += file.Length;
                        }

                        if (!target.Recursive) continue;
                        foreach (var sub in dir.EnumerateDirectories())
                        {
                            // Nunca seguir junctions/links simbólicos: podem apontar para fora da pasta alvo
                            if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                                pending.Push(sub);
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
                    {
                        inaccessible++;
                    }
                }
            }

            return new CleanupScan
            {
                Bytes = bytes,
                ItemCount = files.Count,
                Files = files,
                FolderRoots = roots,
                InaccessibleFolders = inaccessible,
            };
        }
    }
}
