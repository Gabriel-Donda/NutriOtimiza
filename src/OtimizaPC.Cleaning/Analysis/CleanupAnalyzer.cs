using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OtimizaPC.Core;

namespace OtimizaPC.Cleaning.Analysis
{
    /// <summary>Resultado da análise de um item: concluído, com falha ou cancelado.</summary>
    public sealed class CleanupScanOutcome
    {
        private CleanupScanOutcome(Cleaner cleaner, CleanupScan scan, string error, bool wasCancelled)
        {
            Cleaner = cleaner;
            Scan = scan;
            Error = error;
            WasCancelled = wasCancelled;
        }

        public Cleaner Cleaner { get; }

        /// <summary>Null quando o item falhou ou foi cancelado.</summary>
        public CleanupScan Scan { get; }

        /// <summary>Mensagem da falha; null quando não houve erro.</summary>
        public string Error { get; }

        public bool WasCancelled { get; }

        public bool Succeeded => Scan != null;

        internal static CleanupScanOutcome Success(Cleaner cleaner, CleanupScan scan) =>
            new CleanupScanOutcome(cleaner, scan ?? CleanupScan.Empty, null, false);

        internal static CleanupScanOutcome Failed(Cleaner cleaner, string error) =>
            new CleanupScanOutcome(cleaner, null, error ?? "Falha desconhecida", false);

        internal static CleanupScanOutcome Cancelled(Cleaner cleaner) =>
            new CleanupScanOutcome(cleaner, null, null, true);
    }

    /// <summary>
    /// Resultado de uma análise. <see cref="IsComplete"/> só é verdadeiro quando todos os itens
    /// foram analisados: análise cancelada ou com falha não deve habilitar a limpeza como se fosse completa.
    /// </summary>
    public sealed class CleanupAnalysisResult
    {
        internal CleanupAnalysisResult(IReadOnlyList<CleanupScanOutcome> items, bool wasCancelled)
        {
            Items = items;
            WasCancelled = wasCancelled;
        }

        public IReadOnlyList<CleanupScanOutcome> Items { get; }

        public bool WasCancelled { get; }

        public bool HasErrors => Items.Any(i => i.Error != null);

        public bool IsComplete => !WasCancelled && !HasErrors;

        public int ScannedCount => Items.Count(i => i.Succeeded);

        /// <summary>Soma apenas dos itens analisados; não inclui cancelados nem com falha.</summary>
        public long TotalBytes => Items.Where(i => i.Succeeded).Sum(i => i.Scan.Bytes);
    }

    /// <summary>Executa a análise de vários itens com concorrência limitada e cancelamento observável.</summary>
    public static class CleanupAnalyzer
    {
        public const int DefaultConcurrency = 4;

        /// <param name="onItemScanned">
        /// Chamado ao fim de cada item, inclusive cancelados ou com falha, para a interface atualizar o progresso.
        /// </param>
        public static async Task<CleanupAnalysisResult> ScanAsync(
            IEnumerable<Cleaner> cleaners,
            CancellationToken cancellationToken,
            Action<CleanupScanOutcome> onItemScanned = null,
            int maxConcurrency = DefaultConcurrency)
        {
            if (cleaners == null) throw new ArgumentNullException(nameof(cleaners));
            if (maxConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

            var list = cleaners.ToList();
            var outcomes = new CleanupScanOutcome[list.Count];
            var cancelledItems = 0;

            using (var gate = new SemaphoreSlim(maxConcurrency))
            {
                var tasks = list.Select((cleaner, index) => ScanItemAsync(
                    cleaner, index, outcomes, gate, () => Interlocked.Increment(ref cancelledItems),
                    onItemScanned, cancellationToken));
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            return new CleanupAnalysisResult(outcomes, Volatile.Read(ref cancelledItems) > 0);
        }

        private static async Task ScanItemAsync(
            Cleaner cleaner,
            int index,
            CleanupScanOutcome[] outcomes,
            SemaphoreSlim gate,
            Action onCancelled,
            Action<CleanupScanOutcome> onItemScanned,
            CancellationToken cancellationToken)
        {
            var acquired = false;
            CleanupScanOutcome outcome;
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;
                cancellationToken.ThrowIfCancellationRequested();

                var scan = await Task.Run(() => cleaner.Scan(cancellationToken), cancellationToken).ConfigureAwait(false);
                outcome = CleanupScanOutcome.Success(cleaner, scan);
            }
            catch (OperationCanceledException)
            {
                onCancelled();
                outcome = CleanupScanOutcome.Cancelled(cleaner);
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao analisar '{cleaner.Name}'", ex);
                outcome = CleanupScanOutcome.Failed(cleaner, ex.Message);
            }
            finally
            {
                if (acquired) gate.Release();
            }

            outcomes[index] = outcome;
            onItemScanned?.Invoke(outcome);
        }
    }
}
