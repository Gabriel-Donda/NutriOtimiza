using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OtimizaPC.Repair.Diagnostics;
using OtimizaPC.Repair.Fixes;
using OtimizaPC.Repair.Console;
using OtimizaPC.Repair.Tools;
using Xunit;

namespace OtimizaPC.Tests
{
    public sealed class RepairFixTests
    {
        [Fact]
        public void Catalogo_AceitaProviderCaseInsensitiveEAliasesDeDiscoENtfs()
        {
            var disk = EventFixCatalog.For(Summary("MICROSOFT-WINDOWS-DISK", 7));
            var ntfs = EventFixCatalog.For(Summary("mIcRoSoFt-WiNdOwS-NtFs", 55));

            Assert.Contains(disk, fix => fix.Id == "disk-safe-scan");
            Assert.Contains(ntfs, fix => fix.Id == "disk-safe-scan");
        }

        [Theory]
        [InlineData("Microsoft-Windows-DNS-Client", 1014, "dns-1014-flush")]
        [InlineData("Time-Service", 134, "time-service-resync")]
        [InlineData("Microsoft-Windows-WindowsUpdateClient", 20, "windows-update-reset")]
        [InlineData("Microsoft-Windows-GroupPolicy", 1058, "grouppolicy-refresh")]
        [InlineData("Print", 808, "print-spooler-restart")]
        public void Catalogo_OfereceSomenteFixSeguroDaFamilia(string provider, int id, string fixId)
        {
            var fixes = EventFixCatalog.For(Summary(provider, id));

            Assert.Contains(fixes, fix => fix.Id == fixId);
            Assert.True(EventFixCatalog.HasAutomaticFix(Summary(provider, id)));
        }

        [Fact]
        public void Diagnostico_ClassificaDCOM10016ComoInformativoESemFix()
        {
            var summary = Summary("Microsoft-Windows-DistributedCOM", 10016);

            var diagnosis = EventFixCatalog.Diagnosis(summary);

            Assert.Equal(EventDiagnosisClass.Informativo, diagnosis.Classification);
            Assert.Empty(EventFixCatalog.For(summary));
            Assert.Contains("Sem correção automática", diagnosis.Guidance);
        }

        [Fact]
        public void Diagnostico_NenhumEventoCatalogadoUsaTituloGenericoOuRepeteDiagnostico()
        {
            var events = new[]
            {
                Tuple.Create("NETLOGON", 5719), Tuple.Create("NETLOGON", 5805),
                Tuple.Create("Service Control Manager", 7000), Tuple.Create("Service Control Manager", 7001),
                Tuple.Create("Service Control Manager", 7009), Tuple.Create("Service Control Manager", 7011),
                Tuple.Create("Service Control Manager", 7023), Tuple.Create("Service Control Manager", 7026),
                Tuple.Create("Service Control Manager", 7031), Tuple.Create("Service Control Manager", 7034),
                Tuple.Create("BitLocker-Driver", 24641), Tuple.Create("Microsoft-Windows-DistributedCOM", 10016),
                Tuple.Create("Microsoft-Windows-DistributedCOM", 10010), Tuple.Create("Microsoft-Windows-Kernel-Power", 41),
                Tuple.Create("Microsoft-Windows-Kernel-Power", 109), Tuple.Create("disk", 7), Tuple.Create("disk", 11),
                Tuple.Create("disk", 51), Tuple.Create("disk", 153), Tuple.Create("Microsoft-Windows-Ntfs", 55),
                Tuple.Create("Ntfs", 98), Tuple.Create("Microsoft-Windows-WHEA-Logger", 17), Tuple.Create("Microsoft-Windows-WHEA-Logger", 18),
                Tuple.Create("Microsoft-Windows-WHEA-Logger", 19), Tuple.Create("Microsoft-Windows-WHEA-Logger", 47),
                Tuple.Create("Application Error", 1000), Tuple.Create(".NET Runtime", 1026), Tuple.Create("Schannel", 36888),
                Tuple.Create("Time-Service", 134), Tuple.Create("Microsoft-Windows-DNS-Client", 1014),
                Tuple.Create("Microsoft-Windows-WindowsUpdateClient", 20), Tuple.Create("Microsoft-Windows-GroupPolicy", 1058),
                Tuple.Create("Print", 372), Tuple.Create("volsnap", 36), Tuple.Create("Perflib", 1008),
                Tuple.Create("Defrag", 1), Tuple.Create("iScsiPrt", 1), Tuple.Create("storahci", 1),
            };

            foreach (var item in events)
            {
                var diagnosis = EventFixCatalog.Diagnosis(new EventSummary
                {
                    Provider = item.Item1,
                    EventId = item.Item2,
                    Subject = "Componente de teste",
                    Message = "Mensagem específica do evento de teste.",
                    Explanation = "Explicação original do evento.",
                });

                Assert.False(string.IsNullOrWhiteSpace(diagnosis.Title));
                Assert.False(diagnosis.Title.IndexOf("agrupado para análise", StringComparison.OrdinalIgnoreCase) >= 0);
                Assert.False(string.IsNullOrWhiteSpace(diagnosis.PlainText));
                Assert.NotEqual(diagnosis.Title, diagnosis.PlainText);
            }
        }

        [Theory]
        [InlineData("Microsoft-Windows-WHEA-Logger", 18)]
        [InlineData("Microsoft-Windows-Kernel-Power", 41)]
        [InlineData("BitLocker-Driver", 24641)]
        [InlineData("Schannel", 36888)]
        public void Diagnostico_ClassificaFamiliasSemFixAutomatico(string provider, int id)
        {
            var summary = Summary(provider, id);

            var diagnosis = EventFixCatalog.Diagnosis(summary);

            Assert.Empty(EventFixCatalog.For(summary));
            Assert.Contains("Sem correção automática", diagnosis.Guidance);
        }

        [Fact]
        public void Catalogo_NaoAcionaFixComSubstringFracaNaMensagem()
        {
            var summary = Summary("Application Error", 1000);
            summary.Message = "Falha em C:\\Windows\\System32\\kernel32.dll & whoami | format c:";

            var fixes = EventFixCatalog.For(summary);

            Assert.Empty(fixes);
            Assert.Contains("Sem correção automática", EventFixCatalog.Diagnosis(summary).Guidance);
        }

        [Fact]
        public void Catalogo_PreservaSubjectAoRecomendarServicoNaoCritico()
        {
            var summary = Summary("Service Control Manager", 7000);
            summary.Subject = "Spooler";
            summary.Message = "The Spooler service failed to start.";

            var fixes = EventFixCatalog.For(summary);

            if (fixes.Count == 0) return;
            Assert.Contains(fixes, fix => fix.Title.IndexOf("Spooler", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.All(fixes, fix => Assert.True(fix.NeedsConfirmation));
        }

        [Fact]
        public async Task Fix_CanceladoAntesDaExecucaoNaoDeixaProcessoAtivo()
        {
            var fix = EventFixCatalog.For(Summary("Microsoft-Windows-DNS-Client", 1014)).Single();
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => fix.RunAsync(_ => { }, cancellation.Token));
            }
        }

        [Theory]
        [InlineData("C:\\Windows\\System32\\whoami.exe")]
        [InlineData("..\\whoami.exe")]
        [InlineData("subdir/whoami.exe")]
        public void CommandStep_RejeitaNomeRootedOuTraversal(string tool)
        {
            Assert.Throws<ArgumentException>(() => new CommandStep(tool, string.Empty));
        }

        [Fact]
        public void Catalogo_NaoReiniciaServicoCriticoOuForaDaAllowlist()
        {
            var critical = Summary("Service Control Manager", 7000);
            critical.Message = "The RpcSs service failed to start due to an error.";
            critical.Subject = "RpcSs";
            var builtInButNotAllowlisted = Summary("Service Control Manager", 7000);
            builtInButNotAllowlisted.Message = "The wuauserv service failed to start due to an error.";
            builtInButNotAllowlisted.Subject = "wuauserv";

            Assert.Empty(EventFixCatalog.For(critical));
            Assert.Empty(EventFixCatalog.For(builtInButNotAllowlisted));
        }

        [Fact]
        public void Catalogo_NaoExtraiNomeDeServicoDeTokenSolto()
        {
            var summary = Summary("Service Control Manager", 7000);
            summary.Subject = "Spooler";
            summary.Message = "Falha genérica contendo Spooler, mas sem o padrão estruturado do SCM.";

            Assert.Empty(EventFixCatalog.For(summary));
        }

        [Fact]
        public void SfcDeEventoForte_EhMedioENaoPodeSerRecomendado()
        {
            var summary = Summary("Application Error", 1000);
            summary.Message = "Faulting module name: ntdll.dll, version 10.0."
                + " Faulting application name: app.exe";

            var fix = Assert.Single(EventFixCatalog.For(summary), f => f.Id == "application-windows-sfc");

            Assert.Equal(FixRisk.Medium, fix.RiskLevel);
            Assert.True(fix.NeedsConfirmation);
            Assert.False(EventFixPolicy.CanBeRecommended(fix));
        }

        [Fact]
        public void Spooler_RestauraEstadoMesmoQuandoCanceladoDepoisDeParar()
        {
            var calls = new System.Collections.Generic.List<string>();
            using (var cancellation = new CancellationTokenSource())
            {
                Assert.Throws<InvalidOperationException>(() => SpoolerStateMachine.Execute(
                    true,
                    () => { calls.Add("stop"); cancellation.Cancel(); throw new InvalidOperationException("falha de teste"); },
                    () => calls.Add("start"),
                    cancellation.Token));
            }

            Assert.Equal(new[] { "stop", "start" }, calls);
        }

        [Fact]
        public async Task ProcessStreamer_TokenCanceladoAntesDoStartNaoCriaProcesso()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => ProcessStreamer.RunAsync(
                    "arquivo-inexistente-eventfix.exe", string.Empty, _ => { }, cancellation.Token));
            }
        }

        [Fact]
        public void Politica_DominioExigeConfirmacaoParaFixMedio()
        {
            var summary = Summary("Application Error", 1000);
            summary.Message = "Faulting module name: ntdll.dll";
            var fix = EventFixCatalog.For(summary).FirstOrDefault();
            Assert.NotNull(fix);
            Assert.True(EventFixPolicy.RequiresExplicitConfirmation(fix));
            Assert.False(EventFixPolicy.CanRun(fix, false));
            Assert.True(EventFixPolicy.CanRun(fix, true));
        }

        private static EventSummary Summary(string provider, int id)
        {
            return new EventSummary
            {
                Provider = provider,
                EventId = id,
                Subject = "componente de teste",
                Message = "Mensagem de teste sem causa suficiente.",
                Explanation = "Explicação de teste.",
                Count = 1,
            };
        }
    }
}
