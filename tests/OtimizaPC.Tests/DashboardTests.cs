using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OtimizaPC.Core;
using OtimizaPC.Core.Models;
using OtimizaPC.Hardware;
using Xunit;

namespace OtimizaPC.Tests
{
    public sealed class DashboardMetricsTests
    {
        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(25, 100, 75)]
        [InlineData(100, 100, 0)]
        [InlineData(0, 100, 100)]
        [InlineData(10, -5, 0)]
        public void UsagePercent_CalculaPercentualDoIntervalo(double idleDelta, double totalDelta, double expected)
        {
            Assert.Equal(expected, DashboardMetrics.UsagePercent(idleDelta, totalDelta), 3);
        }

        [Theory]
        [InlineData(1.0, 1.0, 8, 12.5)]
        [InlineData(8.0, 1.0, 8, 100)]
        [InlineData(0.5, 1.0, 4, 12.5)]
        [InlineData(1.0, 0.0, 8, 0)]
        [InlineData(-1.0, 1.0, 8, 0)]
        public void ProcessPercent_NormalizaPeloTempoDeParedeENucleos(
            double cpuSeconds, double wallSeconds, int cores, double expected)
        {
            var percent = DashboardMetrics.ProcessPercent(TimeSpan.FromSeconds(cpuSeconds), wallSeconds, cores);

            Assert.Equal(expected, percent, 3);
        }

        [Fact]
        public void DiskUsageItem_CalculaUsoERotulos()
        {
            var disk = DiskUsageItem.Create(@"C:\", "Windows", 1000, 250);

            Assert.Equal("C:", disk.Name);
            Assert.Equal(750, disk.UsedBytes);
            Assert.Equal(75, disk.UsedPercent, 3);
            Assert.Contains("Windows", disk.Label);
            Assert.Contains("C:", disk.Title);
            Assert.Contains("75", disk.UsedText);
        }

        [Fact]
        public void DiskUsageItem_SemRotuloUsaSoADrive()
        {
            var disk = DiskUsageItem.Create("D:", null, 1000, 1000);

            Assert.Equal("D:", disk.Title);
            Assert.Equal(0, disk.UsedPercent, 3);
        }

        [Fact]
        public void DiskUsageItem_TotalZeroNaoDividePorZero()
        {
            var disk = DiskUsageItem.Create("E:", "Backup", 0, 0);

            Assert.Equal(0, disk.UsedPercent);
            Assert.Equal(0, disk.UsedBytes);
        }

        [Fact]
        public void ProcessUsageItem_StatusPorCpu()
        {
            var item = new ProcessUsageItem("app", 85, 100, 16UL * 1024 * 1024 * 1024);

            Assert.Equal(HealthStatus.Critical, item.Status);
            Assert.Contains("85", item.CpuText);
        }

        [Fact]
        public void ProcessUsageItem_StatusPorMemoriaRelativa()
        {
            const ulong total = 8UL * 1024 * 1024 * 1024;
            var item = new ProcessUsageItem("app", 1, (long)(total * 0.6), total);

            Assert.Equal(HealthStatus.Critical, item.Status);
        }

        [Fact]
        public void ProcessUsageItem_StatusOkQuandoConsumoBaixo()
        {
            var item = new ProcessUsageItem("app", 5, 64L * 1024 * 1024, 16UL * 1024 * 1024 * 1024);

            Assert.Equal(HealthStatus.Ok, item.Status);
        }

        [Fact]
        public void NativeLayout_SystemProcessorPerformanceInformation_Tem48Bytes()
        {
            var type = typeof(DashboardMetrics).Assembly.GetType(
                "OtimizaPC.Hardware.Interop.NativeMethods+SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION");

            Assert.NotNull(type);
            Assert.Equal(48, System.Runtime.InteropServices.Marshal.SizeOf(type));
        }

        [Fact]
        public void GetCpuCores_NaoLancaEPercentuaisFicamNaFaixa()
        {
            var metrics = new DashboardMetrics();
            metrics.GetCpuCores();

            var cores = metrics.GetCpuCores();

            Assert.Equal(Environment.ProcessorCount, cores.Count);
            Assert.All(cores, usage => Assert.InRange(usage, 0, 100));
        }

        [Fact]
        public void SyncCores_SegundaAmostraComMesmaContagemAtualizaOsItens()
        {
            var target = new List<CpuCoreUsage>();

            Assert.True(DashboardMetrics.SyncCores(target, new[] { 10.0, 20.0, 30.0 }));
            var first = target.ToArray();

            // Mesma contagem na 2ª amostra: os itens NÃO podem ficar congelados
            Assert.True(DashboardMetrics.SyncCores(target, new[] { 55.0, 65.0, 75.0 }));

            Assert.Equal(3, target.Count);
            Assert.Same(first[0], target[0]);
            Assert.Same(first[1], target[1]);
            Assert.Same(first[2], target[2]);
            Assert.Equal(55.0, target[0].Usage, 3);
            Assert.Equal(65.0, target[1].Usage, 3);
            Assert.Equal(75.0, target[2].Usage, 3);
        }

        [Fact]
        public void SyncCores_ContagemDiferenteRecriaOsItens()
        {
            var target = new List<CpuCoreUsage>();
            DashboardMetrics.SyncCores(target, new[] { 10.0, 20.0 });

            Assert.True(DashboardMetrics.SyncCores(target, new[] { 1.0, 2.0, 3.0, 4.0 }));

            Assert.Equal(4, target.Count);
            Assert.Equal(new[] { 0, 1, 2, 3 }, target.Select(c => c.Index));
        }

        [Fact]
        public void SyncCores_AmostraVaziaNaoApagaOsItens()
        {
            var target = new List<CpuCoreUsage>();
            DashboardMetrics.SyncCores(target, new[] { 10.0, 20.0 });

            Assert.False(DashboardMetrics.SyncCores(target, new double[0]));
            Assert.False(DashboardMetrics.SyncCores(target, null));

            Assert.Equal(2, target.Count);
            Assert.Equal(10.0, target[0].Usage, 3);
        }

        [Fact]
        public void SyncCores_SemMudancaRetornaFalse()
        {
            var target = new List<CpuCoreUsage>();
            DashboardMetrics.SyncCores(target, new[] { 10.0, 20.0 });

            Assert.False(DashboardMetrics.SyncCores(target, new[] { 10.0, 20.0 }));
        }

        [Fact]
        public void GetCpuCores_SegundaAmostraMantemAContagemEAsInstancias()
        {
            var metrics = new DashboardMetrics();
            var target = new List<CpuCoreUsage>();

            Assert.True(DashboardMetrics.SyncCores(target, metrics.GetCpuCores()));
            var first = target.ToArray();

            Thread.Sleep(50);
            DashboardMetrics.SyncCores(target, metrics.GetCpuCores());

            Assert.Equal(first.Length, target.Count);
            Assert.Same(first[0], target[0]);
            Assert.All(target, core => Assert.InRange(core.Usage, 0, 100));
        }

        [Fact]
        public void GetSystemDrive_DevolveUnidadeLegivel()
        {
            var drive = new DashboardMetrics().GetSystemDrive();

            Assert.NotNull(drive);
            Assert.InRange(drive.UsedPercent, 0, 100);
            Assert.True(drive.TotalBytes > 0);
        }

        [Fact]
        public void GetDisks_NaoLancaERetornaListaValida()
        {
            var disks = new DashboardMetrics().GetDisks();

            Assert.All(disks, disk => Assert.InRange(disk.UsedPercent, 0, 100));
        }

        [Fact]
        public void GetTopProcesses_LimitaAosCinco()
        {
            var snapshot = new DashboardMetrics().GetTopProcesses(5);

            Assert.True(snapshot.ByCpu.Count <= 5);
            Assert.True(snapshot.ByMemory.Count <= 5);
        }
    }

    public sealed class DashboardSummaryTests
    {
        [Fact]
        public void Build_SemDados_MantemTodosOsRotulosComFallback()
        {
            var entries = DashboardSummary.Build(new SummaryData());

            Assert.Equal(
                new[]
                {
                    "Computador", "Sistema", "BIOS", "Placa-mãe", "Processador", "Núcleos / threads",
                    "Memória RAM", "Vídeo", "Discos", "Rede", "Resolução", "Bateria", "Último boot",
                },
                entries.Select(e => e.Label));

            Assert.All(entries, e => Assert.Equal(DashboardSummary.Unavailable, e.Value));
        }

        [Fact]
        public void Build_ComDados_MontaResumoEstendido()
        {
            var boot = new DateTime(2026, 10, 23, 8, 30, 0);
            var entries = DashboardSummary.Build(new SummaryData
            {
                Manufacturer = "Dell Inc.",
                Model = "Latitude 5420",
                OsCaption = "Microsoft Windows 11 Pro",
                OsVersion = "24H2 (build 26100.2161)",
                BiosVendor = "Dell Inc.",
                BiosVersion = "1.2.3",
                BoardManufacturer = "Dell Inc.",
                BoardProduct = "0ABCDE",
                CpuName = "12th Gen Intel(R) Core(TM) i5-1145G7",
                Cores = 4,
                Threads = 8,
                MemoryTotal = 16UL * 1024 * 1024 * 1024,
                MemorySticks = 1,
                MemorySlots = 2,
                Gpu = "Intel(R) Iris(R) Xe Graphics",
                Disks = "C: 120 GB livres de 256 GB",
                Network = "10.0.0.15",
                Resolution = "1920 × 1080",
                Battery = "87% (Na tomada)",
                LastBoot = boot,
            });

            Assert.Equal("Dell Inc. Latitude 5420", Value(entries, "Computador"));
            Assert.Equal("Microsoft Windows 11 Pro — 24H2 (build 26100.2161)", Value(entries, "Sistema"));
            Assert.Equal("Dell Inc. 1.2.3", Value(entries, "BIOS"));
            Assert.Equal("Dell Inc. 0ABCDE", Value(entries, "Placa-mãe"));
            Assert.Equal("4 núcleos / 8 threads", Value(entries, "Núcleos / threads"));
            Assert.Equal(
                Format.Bytes(16UL * 1024 * 1024 * 1024) + " — 1 de 2 slots",
                Value(entries, "Memória RAM"));
            Assert.Equal("10.0.0.15", Value(entries, "Rede"));
            Assert.Equal("1920 × 1080", Value(entries, "Resolução"));
            Assert.Equal("87% (Na tomada)", Value(entries, "Bateria"));
            Assert.Equal(Format.Date(boot, withTime: true), Value(entries, "Último boot"));
        }

        [Fact]
        public void Build_UmDadoPorVez_NaoInventaRotulos()
        {
            var cores = DashboardSummary.Build(new SummaryData { Cores = 8 });
            Assert.Equal("8 núcleos", Value(cores, "Núcleos / threads"));
            Assert.Equal(DashboardSummary.Unavailable, Value(cores, "Processador"));

            var threads = DashboardSummary.Build(new SummaryData { Threads = 16 });
            Assert.Equal("16 threads", Value(threads, "Núcleos / threads"));
        }

        [Fact]
        public void Build_MemoriaSemSlotsNaoMostraParenteses()
        {
            var entries = DashboardSummary.Build(new SummaryData { MemoryTotal = 8UL * 1024 * 1024 * 1024 });

            Assert.Equal(Format.Bytes(8UL * 1024 * 1024 * 1024), Value(entries, "Memória RAM"));
        }

        private static string Value(System.Collections.Generic.IEnumerable<InfoEntry> entries, string label) =>
            entries.Single(e => e.Label == label).Value;
    }
}
