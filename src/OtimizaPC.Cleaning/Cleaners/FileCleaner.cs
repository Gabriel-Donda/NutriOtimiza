using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OtimizaPC.Cleaning.Files;

namespace OtimizaPC.Cleaning.Cleaners
{
    /// <summary>Limpador baseado em pastas/arquivos.</summary>
    public class FileCleaner : Cleaner
    {
        public FileCleaner(string id, string name, string description, string group, IEnumerable<FileTarget> targets,
            CleanupRisk risk = CleanupRisk.Safe, bool requiresAdmin = false, bool defaultSelected = true)
            : base(id, name, description, group, risk, requiresAdmin, defaultSelected)
        {
            Targets = targets.ToList();
        }

        public IReadOnlyList<FileTarget> Targets { get; }

        public override bool IsAvailable => Targets.Any(t => Directory.Exists(t.Root));

        public override CleanupScan Scan(CancellationToken cancellationToken) =>
            FileScanner.Scan(Targets, cancellationToken);

        public override CleanResult Clean(CleanupScan scan, CancellationToken cancellationToken) =>
            FileDeleter.Delete(scan, cancellationToken);
    }
}
