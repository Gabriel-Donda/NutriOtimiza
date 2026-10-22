using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OtimizaPC.Cleaning.Analysis
{
    /// <summary>Pasta na árvore de análise. Guarda só o nome para economizar memória em discos grandes.</summary>
    public sealed class FolderNode
    {
        public FolderNode(string name, FolderNode parent)
        {
            Name = name;
            Parent = parent;
        }

        public string Name { get; }

        public FolderNode Parent { get; }

        public string FullPath => Parent == null ? Name : Path.Combine(Parent.FullPath, Name);

        /// <summary>Tamanho total (arquivos diretos + subpastas).</summary>
        public long Size { get; internal set; }

        /// <summary>Tamanho só dos arquivos que estão diretamente nesta pasta.</summary>
        public long DirectFilesSize { get; internal set; }

        public int DirectFileCount { get; internal set; }

        /// <summary>Total de arquivos, incluindo subpastas.</summary>
        public long FileCount { get; internal set; }

        public bool IsAccessible { get; internal set; } = true;

        public List<FolderNode> Children { get; } = new List<FolderNode>();
    }

    public sealed class LargeFile
    {
        public LargeFile(string path, long size)
        {
            Path = path;
            Size = size;
        }

        public string Path { get; }

        public long Size { get; }
    }

    public sealed class DiskAnalysis
    {
        public FolderNode Root { get; set; }

        public IReadOnlyList<LargeFile> LargestFiles { get; set; }

        public int InaccessibleFolders { get; set; }

        public TimeSpan Duration { get; set; }
    }

    /// <summary>Mapeia o uso de espaço de uma pasta/unidade (estilo WinDirStat/TreeSize).</summary>
    public sealed class DiskAnalyzer
    {
        private const int LargestFilesToKeep = 100;
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(150);

        private readonly object _largestSync = new object();
        private readonly SortedSet<(long Size, string Path)> _largest = new SortedSet<(long, string)>();
        private readonly Stopwatch _progressClock = Stopwatch.StartNew();
        private IProgress<string> _progress;
        private int _inaccessible;
        private long _lastProgressTicks;

        public static Task<DiskAnalysis> AnalyzeAsync(string rootPath, IProgress<string> progress, CancellationToken cancellationToken) =>
            Task.Run(() => new DiskAnalyzer().Analyze(rootPath, progress, cancellationToken), cancellationToken);

        private DiskAnalysis Analyze(string rootPath, IProgress<string> progress, CancellationToken cancellationToken)
        {
            _progress = progress;
            var watch = Stopwatch.StartNew();
            var root = new FolderNode(rootPath, null);
            var rootDir = new DirectoryInfo(rootPath);

            // Arquivos da raiz no próprio thread; subpastas de primeiro nível em paralelo
            ScanFiles(rootDir, root);
            var firstLevel = SafeDirectories(rootDir, root)
                .Select(d => (Dir: d, Node: new FolderNode(d.Name, root)))
                .ToList();

            Parallel.ForEach(firstLevel,
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
                item => ScanTree(item.Dir, item.Node, cancellationToken));

            foreach (var item in firstLevel) root.Children.Add(item.Node);
            Aggregate(root);

            List<LargeFile> largest;
            lock (_largestSync)
                largest = _largest.Reverse().Select(f => new LargeFile(f.Path, f.Size)).ToList();

            return new DiskAnalysis
            {
                Root = root,
                LargestFiles = largest,
                InaccessibleFolders = _inaccessible,
                Duration = watch.Elapsed,
            };
        }

        private void ScanTree(DirectoryInfo dir, FolderNode node, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportProgress(dir.FullName);
            ScanFiles(dir, node);

            foreach (var sub in SafeDirectories(dir, node))
            {
                var child = new FolderNode(sub.Name, node);
                node.Children.Add(child);
                ScanTree(sub, child, cancellationToken);
            }
        }

        private void ScanFiles(DirectoryInfo dir, FolderNode node)
        {
            try
            {
                foreach (var file in dir.EnumerateFiles())
                {
                    var length = file.Length;
                    node.DirectFilesSize += length;
                    node.DirectFileCount++;
                    TrackLargest(file.FullName, length);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
            {
                MarkInaccessible(node);
            }
        }

        private IEnumerable<DirectoryInfo> SafeDirectories(DirectoryInfo dir, FolderNode node)
        {
            try
            {
                // Links simbólicos/junctions são ignorados para não contar o mesmo espaço duas vezes
                return dir.EnumerateDirectories()
                    .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                    .ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
            {
                MarkInaccessible(node);
                return Enumerable.Empty<DirectoryInfo>();
            }
        }

        private void MarkInaccessible(FolderNode node)
        {
            if (!node.IsAccessible) return;
            node.IsAccessible = false;
            Interlocked.Increment(ref _inaccessible);
        }

        private void TrackLargest(string path, long size)
        {
            lock (_largestSync)
            {
                if (_largest.Count >= LargestFilesToKeep && size <= _largest.Min.Size) return;
                _largest.Add((size, path));
                if (_largest.Count > LargestFilesToKeep) _largest.Remove(_largest.Min);
            }
        }

        private void ReportProgress(string path)
        {
            if (_progress == null) return;
            var now = _progressClock.Elapsed.Ticks;
            var last = Interlocked.Read(ref _lastProgressTicks);
            if (now - last < ProgressInterval.Ticks) return;
            if (Interlocked.CompareExchange(ref _lastProgressTicks, now, last) == last)
                _progress.Report(path);
        }

        /// <summary>Soma tamanhos de baixo para cima e ordena as subpastas da maior para a menor.</summary>
        private static void Aggregate(FolderNode node)
        {
            long size = node.DirectFilesSize;
            long count = node.DirectFileCount;
            foreach (var child in node.Children)
            {
                Aggregate(child);
                size += child.Size;
                count += child.FileCount;
            }
            node.Size = size;
            node.FileCount = count;
            node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        }
    }
}
