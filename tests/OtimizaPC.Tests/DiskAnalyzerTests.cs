using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OtimizaPC.Cleaning.Analysis;
using Xunit;

namespace OtimizaPC.Tests
{
    public class DiskAnalyzerTests
    {
        [Fact]
        public async Task Analyze_SomaTamanhosEOrdenaPorTamanho()
        {
            using (var temp = new TempFolder())
            {
                temp.File("raiz.bin", 10);
                temp.File(@"pequena\a.bin", 100);
                temp.File(@"grande\b.bin", 5000);
                temp.File(@"grande\sub\c.bin", 3000);

                var analysis = await DiskAnalyzer.AnalyzeAsync(temp.Path, null, CancellationToken.None);

                Assert.Equal(8110, analysis.Root.Size);
                Assert.Equal(4, analysis.Root.FileCount);
                Assert.Equal(new[] { "grande", "pequena" }, analysis.Root.Children.Select(c => c.Name));
                Assert.Equal(8000, analysis.Root.Children[0].Size);
                Assert.Equal(5000, analysis.LargestFiles[0].Size);
            }
        }

        [Fact]
        public async Task Analyze_NaoContaJunctionDuasVezes()
        {
            using (var outside = new TempFolder())
            using (var temp = new TempFolder())
            {
                outside.File("enorme.bin", 9000);
                temp.File("pequeno.bin", 10);
                temp.Junction("link", outside.Path);

                var analysis = await DiskAnalyzer.AnalyzeAsync(temp.Path, null, CancellationToken.None);

                Assert.Equal(10, analysis.Root.Size);
            }
        }
    }
}
