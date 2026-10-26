using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OtimizaPC.Cleaning;
using OtimizaPC.Cleaning.Analysis;
using Xunit;

namespace OtimizaPC.Tests
{
    public class CleanupAnalysisTests
    {
        private sealed class FakeCleaner : Cleaner
        {
            private readonly Func<CancellationToken, CleanupScan> _scan;

            public FakeCleaner(string id, Func<CancellationToken, CleanupScan> scan)
                : base(id, id, "item de teste", CleanupGroups.Windows)
            {
                _scan = scan;
            }

            public override CleanupScan Scan(CancellationToken cancellationToken) => _scan(cancellationToken);

            public override CleanResult Clean(CleanupScan scan, CancellationToken cancellationToken) => new CleanResult();
        }

        private static CleanupScan ScanOf(long bytes) => new CleanupScan { Bytes = bytes, ItemCount = 1 };

        [Fact]
        public async Task Analise_CompletaAgregaTotais()
        {
            var result = await CleanupAnalyzer.ScanAsync(new[]
            {
                new FakeCleaner("a", _ => ScanOf(100)),
                new FakeCleaner("b", _ => ScanOf(250)),
            }, CancellationToken.None);

            Assert.True(result.IsComplete);
            Assert.False(result.WasCancelled);
            Assert.False(result.HasErrors);
            Assert.Equal(350, result.TotalBytes);
            Assert.Equal(2, result.ScannedCount);
        }

        [Fact]
        public async Task Analise_CanceladaNaoEhConcluida()
        {
            using (var cts = new CancellationTokenSource())
            {
                var result = await CleanupAnalyzer.ScanAsync(new Cleaner[]
                {
                    new FakeCleaner("rapido", _ => ScanOf(500)),
                    new FakeCleaner("lento", token =>
                    {
                        token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                        token.ThrowIfCancellationRequested();
                        return ScanOf(999);
                    }),
                }, cts.Token, outcome =>
                {
                    if (outcome.Cleaner.Id == "rapido") cts.Cancel();
                });

                Assert.True(result.WasCancelled);
                Assert.False(result.IsComplete);
                Assert.False(result.HasErrors);
                Assert.Equal(1, result.ScannedCount);
                Assert.Equal(500, result.TotalBytes);

                var slow = result.Items.Single(i => i.Cleaner.Id == "lento");
                Assert.True(slow.WasCancelled);
                Assert.False(slow.Succeeded);
            }
        }

        [Fact]
        public async Task Analise_ComTokenJaCanceladoMarcaCancelada()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                var result = await CleanupAnalyzer.ScanAsync(new Cleaner[]
                {
                    new FakeCleaner("a", _ => ScanOf(1)),
                }, cts.Token);

                Assert.True(result.WasCancelled);
                Assert.False(result.IsComplete);
                Assert.Equal(0, result.ScannedCount);
                Assert.Equal(0, result.TotalBytes);
            }
        }

        [Fact]
        public async Task Analise_FalhaDeUmItemNaoCancelaOsDemais()
        {
            var result = await CleanupAnalyzer.ScanAsync(new Cleaner[]
            {
                new FakeCleaner("ok", _ => ScanOf(300)),
                new FakeCleaner("quebrado", _ => throw new InvalidOperationException("disco indisponível")),
            }, CancellationToken.None);

            Assert.False(result.WasCancelled);
            Assert.True(result.HasErrors);
            Assert.False(result.IsComplete);
            Assert.Equal(300, result.TotalBytes);

            var failed = result.Items.Single(i => i.Cleaner.Id == "quebrado");
            Assert.False(failed.Succeeded);
            Assert.False(failed.WasCancelled);
            Assert.Equal("disco indisponível", failed.Error);
        }
    }
}
