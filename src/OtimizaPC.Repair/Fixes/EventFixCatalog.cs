using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OtimizaPC.Core;
using OtimizaPC.Repair.Console;
using OtimizaPC.Repair.Diagnostics;
using OtimizaPC.Repair.Tools;

namespace OtimizaPC.Repair.Fixes
{
    public enum EventDiagnosisClass
    {
        Real,
        Informativo,
        Hardware,
    }

    public sealed class EventDiagnosis
    {
        public EventDiagnosisClass Classification { get; set; }
        public string Title { get; set; }
        public string PlainText { get; set; }
        public string Evidence { get; set; }
        public string Verification { get; set; }
        public string Guidance { get; set; }
    }

    public enum FixRisk
    {
        None,
        Low,
        Medium,
    }

    public sealed class EventFix
    {
        private readonly Func<Action<OutputLine>, CancellationToken, Task<RepairResult>> _run;

        internal EventFix(string id, string title, string description, string risk, FixRisk riskLevel,
            bool requiresAdmin, bool requiresRestart, bool needsConfirmation,
            Func<Action<OutputLine>, CancellationToken, Task<RepairResult>> run)
        {
            Id = id;
            Title = title;
            Description = description;
            Risk = risk;
            RiskLevel = riskLevel;
            RequiresAdmin = requiresAdmin;
            RequiresRestart = requiresRestart;
            NeedsConfirmation = riskLevel == FixRisk.Medium || needsConfirmation;
            _run = run;
        }

        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public string Risk { get; }
        public FixRisk RiskLevel { get; }
        public bool RequiresAdmin { get; }
        public bool RequiresRestart { get; }
        public bool NeedsConfirmation { get; }

        public Task<RepairResult> RunAsync(Action<OutputLine> output, CancellationToken ct) => RunAsync(output, ct, false);

        public Task<RepairResult> RunAsync(Action<OutputLine> output, CancellationToken ct, bool confirmationGranted)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (_run == null) throw new InvalidOperationException("Este reparo não possui execução configurada.");
            ct.ThrowIfCancellationRequested();
            if (RequiresAdmin && !AppInfo.IsAdministrator)
                throw new UnauthorizedAccessException("Este reparo exige execução como administrador.");
            if (!EventFixPolicy.CanRun(this, confirmationGranted))
                throw new InvalidOperationException("Este reparo exige confirmação explícita antes da execução.");
            return _run(output, ct);
        }
    }

    /// <summary>Política única para impedir que chamadas fora da UI contornem confirmação e lote seguro.</summary>
    public static class EventFixPolicy
    {
        public static bool RequiresExplicitConfirmation(EventFix fix)
        {
            if (fix == null) return true;
            return fix.NeedsConfirmation || fix.RequiresRestart || fix.RiskLevel == FixRisk.Medium;
        }

        public static bool CanRun(EventFix fix, bool confirmationGranted)
        {
            if (fix == null) return false;
            return !RequiresExplicitConfirmation(fix) || confirmationGranted;
        }

        public static bool CanBeRecommended(EventFix fix)
        {
            return fix != null && !RequiresExplicitConfirmation(fix)
                && fix.RiskLevel != FixRisk.Medium && !fix.RequiresRestart;
        }
    }

    public static class EventFixCatalog
    {
        private static readonly Regex ServiceName = new Regex(@"^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant);
        private static readonly Regex ServiceFromMessage = new Regex(
            @"(?:the\s+|o\s+)?(?<name>[A-Za-z0-9_.-]+)\s+(?:service|serviço)\s+(?:failed|terminated|could\s+not|couldn't|não|nao|falhou|foi|to\s+connect)|(?:service|serviço)\s+(?<name>[A-Za-z0-9_.-]+)\s+(?:failed|falhou|não|nao)|(?:waiting\s+for|aguardando\s+o)\s+(?:the\s+|o\s+)?(?<name>[A-Za-z0-9_.-]+)\s+(?:service|serviço)|(?:response\s+from|resposta\s+de)\s+(?:the\s+|o\s+)?(?<name>[A-Za-z0-9_.-]+)\s+(?:service|serviço)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> RestartableServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Spooler", "Audiosrv", "AudioEndpointBuilder", "Themes", "BITS", "W32Time", "WSearch", "Fax", "MapsBroker", "WMPNetworkSvc",
        };

        public static IReadOnlyList<EventFix> For(EventSummary summary)
        {
            if (summary == null) return Array.Empty<EventFix>();
            var provider = NormalizeProvider(summary.Provider);
            var fixes = new List<EventFix>();

            if (provider == "dns-client" && summary.EventId == 1014)
                fixes.Add(CommandFix("dns-1014-flush", "Limpar cache de DNS",
                    "Limpa o cache local de DNS sem alterar servidores ou configurações de rede.",
                    "baixo — apenas remove respostas DNS armazenadas localmente", FixRisk.Low, false, false,
                    new[] { new CommandStep("ipconfig.exe", "/flushdns") }, true));

            if (provider == "time-service" && IsOneOf(summary.EventId, 129, 134, 36))
            {
                fixes.Add(CommandFix("time-service-resync", "Sincronizar o relógio",
                    "Consulta o estado do serviço e solicita uma nova sincronização com a hierarquia de hora existente.",
                    "baixo — não troca o servidor NTP", FixRisk.Low, true, false,
                    new[]
                    {
                        new CommandStep("w32tm.exe", "/query /status"),
                        new CommandStep("w32tm.exe", "/resync /force"),
                    }, true));
                fixes.Add(CommandFix("time-service-start", "Iniciar o serviço de hora",
                    "Inicia o serviço Windows Time quando ele está parado; a configuração atual é preservada.",
                    "médio — altera o estado de um serviço", FixRisk.Medium, true, false,
                    new[] { new CommandStep("sc.exe", "start w32time") }));
            }

            if ((provider == "disk" && IsOneOf(summary.EventId, 7, 11, 51, 153)) ||
                (provider == "ntfs" && IsOneOf(summary.EventId, 55, 98)))
                fixes.Add(CommandFix("disk-safe-scan", "Verificar o sistema de arquivos",
                    "Executa uma verificação online e não destrutiva na unidade do sistema.",
                    "baixo — não usa /f nem /r e não agenda reinício", FixRisk.Low, true, false,
                    new[] { new CommandStep("chkdsk.exe", "%SystemDrive% /scan") { SuccessCodes = new[] { 0, 1 } } }));

            if ((provider == "grouppolicy" && IsOneOf(summary.EventId, 1058, 1030)) || IsRelatedWinlogon(summary))
                fixes.Add(CommandFix("grouppolicy-refresh", "Atualizar as políticas de grupo",
                    "Força uma nova aplicação das políticas sem editar o Registro nem substituir a política local.",
                    "baixo — reaplica políticas existentes", FixRisk.Low, true, false,
                    new[] { new CommandStep("gpupdate.exe", "/force") }));

            if (provider == "windowsupdateclient" && IsOneOf(summary.EventId, 20, 25, 31))
                fixes.Add(FromTool("windows-update-reset", "Redefinir componentes do Windows Update",
                    "Para temporariamente os serviços envolvidos e renomeia os caches para que o Windows os recrie; nada é apagado.",
                    "médio — interrompe serviços de atualização e pode exigir nova verificação", FixRisk.Medium, true, false,
                    new WindowsUpdateResetTool()));

            if (provider == "print" && IsOneOf(summary.EventId, 372, 808))
                fixes.Add(FromTool("print-spooler-restart", "Reiniciar a fila de impressão",
                    "Reinicia o Spooler para liberar uma fila travada. Trabalhos pendentes podem precisar ser reenviados.",
                    "médio — reinicia o serviço e pode afetar trabalhos da fila", FixRisk.Medium, true, false,
                    new SpoolerRestartTool()));

            if (provider == "application error" && summary.EventId == 1000 && WindowsModuleIsIndicated(summary))
            {
                fixes.Add(CommandFix("application-windows-sfc", "Verificar arquivos do Windows (SFC)",
                    "Verifica e tenta reparar arquivos do Windows quando a mensagem aponta para um módulo do sistema.",
                    "médio — pode substituir arquivos protegidos do Windows", FixRisk.Medium, true, false,
                    new[] { new CommandStep("sfc.exe", "/scannow") }));
                fixes.Add(CommandFix("application-windows-dism", "Verificar a imagem do Windows (DISM)",
                    "Repara o repositório de componentes quando a mensagem aponta para um módulo do sistema.",
                    "médio — pode baixar e substituir componentes do Windows", FixRisk.Medium, true, false,
                    new[] { new CommandStep("dism.exe", "/Online /Cleanup-Image /RestoreHealth") }));
            }

            if (IsServiceControlManager(provider) && IsOneOf(summary.EventId, 7000, 7001, 7009, 7011, 7023, 7026, 7031, 7034))
            {
                var service = ExtractSafeServiceName(summary);
                var snapshot = service == null ? null : ReadServiceSnapshot(service, summary);
                if (snapshot != null)
                    fixes.Add(FromTool("service-start-" + service.ToLowerInvariant(), "Iniciar o serviço " + service,
                        "Consulta e inicia o serviço identificado na mensagem do evento, sem alterar seu tipo de inicialização.",
                        "médio — pode causar indisponibilidade temporária do serviço", FixRisk.Medium, true, false,
                        new ServiceStartTool(service, snapshot.DisplayName, snapshot.ImagePath)));
            }

            return fixes;
        }

        public static EventDiagnosis Diagnosis(EventSummary summary)
        {
            if (summary == null)
                return new EventDiagnosis
                {
                    Classification = EventDiagnosisClass.Informativo,
                    PlainText = "Não há evento suficiente para diagnosticar.",
                    Evidence = "Resumo ausente.",
                    Verification = "Colete o evento novamente antes de executar qualquer ação.",
                    Guidance = "Sem correção automática: orientar a coleta de provedor, ID, assunto e mensagem.",
                };

            var provider = NormalizeProvider(summary.Provider);
            var diagnosis = new EventDiagnosis
            {
                Classification = Classify(provider, summary),
                Title = EventTitle(provider, summary),
                PlainText = PlainText(provider, summary),
                Evidence = BuildEvidence(summary),
                Verification = Verification(provider, summary),
            };
            if (For(summary).Count == 0)
                diagnosis.Guidance = Guidance(provider, summary);
            return diagnosis;
        }

        public static bool HasAutomaticFix(EventSummary summary) => For(summary).Count > 0;

        private static EventFix CommandFix(string id, string title, string description, string risk, FixRisk riskLevel,
            bool requiresAdmin, bool requiresRestart, IReadOnlyList<CommandStep> steps, bool impact = false)
        {
            var tool = new CommandTool(id, title, description, RepairGroups.Windows, steps);
            tool.RequiresAdmin = requiresAdmin;
            tool.RequiresRestart = requiresRestart;
            tool.NeedsConfirmation = impact || riskLevel == FixRisk.Medium;
            return FromTool(id, title, description, risk, riskLevel, requiresAdmin, requiresRestart, tool);
        }

        private static EventFix FromTool(string id, string title, string description, string risk, FixRisk riskLevel,
            bool requiresAdmin, bool requiresRestart, RepairTool tool)
        {
            return new EventFix(id, title, description, risk, riskLevel, requiresAdmin, requiresRestart,
                riskLevel == FixRisk.Medium || tool.NeedsConfirmation,
                (output, ct) => tool.RunAsync(output, ct));
        }

        private static bool IsOneOf(int value, params int[] values) => values.Contains(value);

        private static string NormalizeProvider(string provider)
        {
            var value = (provider ?? string.Empty).Trim();
            if (value.StartsWith("Microsoft-Windows-", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("Microsoft-Windows-".Length);
            return value.ToLowerInvariant();
        }

        private static bool IsServiceControlManager(string provider) =>
            provider == "service control manager" || provider == "scm";

        private static bool IsRelatedWinlogon(EventSummary summary) =>
            NormalizeProvider(summary.Provider) == "winlogon" && ContainsAny(summary.Message, "group policy", "política de grupo", "politica de grupo", "sysvol", "netlogon");

        private static bool WindowsModuleIsIndicated(EventSummary summary)
        {
            if (summary == null || NormalizeProvider(summary.Provider) != "application error" || summary.EventId != 1000)
                return false;
            var match = Regex.Match(summary.Message ?? string.Empty,
                @"(?:faulting\s+module\s+name|nome\s+do\s+m[óo]dulo\s+com\s+falha)\s*:\s*(?<module>[A-Za-z0-9_.-]+\.(?:dll|exe))",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) return false;
            var module = match.Groups["module"].Value;
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ntdll.dll", "kernel32.dll", "kernelbase.dll", "ntoskrnl.exe"
            }.Contains(module);
        }

        private static string ExtractSafeServiceName(EventSummary summary)
        {
            if (summary == null || !IsServiceControlManager(NormalizeProvider(summary.Provider))) return null;
            var match = ServiceFromMessage.Match(summary.Message ?? string.Empty);
            if (!match.Success) return null;
            var name = match.Groups["name"].Value;
            return ServiceName.IsMatch(name) ? name : null;
        }

        private static ServiceSnapshot ReadServiceSnapshot(string serviceName, EventSummary summary)
        {
            if (!ServiceName.IsMatch(serviceName)) return null;
            try
            {
                var snapshot = ServiceSnapshot.Read(serviceName);
                if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.DisplayName)) return null;
                return RestartableServices.Contains(serviceName) || snapshot.IsThirdParty ? snapshot : null;
            }
            catch (InvalidOperationException) { return null; }
            catch (Win32Exception) { return null; }
        }

        private static EventDiagnosisClass Classify(string provider, EventSummary summary)
        {
            if (provider == "distributedcom" && summary.EventId == 10016) return EventDiagnosisClass.Informativo;
            if (provider == "disk" || provider == "ntfs" || provider == "whea-logger" || provider == "kernel-power") return EventDiagnosisClass.Hardware;
            if (provider == "distributedcom" && summary.EventId == 10010) return EventDiagnosisClass.Informativo;
            if (provider == "kernel-power" && summary.EventId == 109) return EventDiagnosisClass.Informativo;
            return EventDiagnosisClass.Real;
        }

        private static string PlainText(string provider, EventSummary summary)
        {
            if (provider == "netlogon" && summary.EventId == 5719) return "O computador não encontrou um controlador de domínio no logon; verifique rede, DNS e o canal seguro.";
            if (provider == "netlogon" && summary.EventId == 5805) return "O computador não conseguiu autenticar o canal seguro com o domínio; verifique DNS, horário e o canal seguro.";
            if (IsServiceControlManager(provider)) return ServicePlainText(summary);
            if (provider == "bitlocker-driver") return "Uma operação do BitLocker falhou ou ficou pendente; a causa pode estar na proteção, TPM, volume ou política.";
            if (provider == "distributedcom" && summary.EventId == 10016) return "Uma permissão DCOM foi registrada; esse aviso costuma não ter impacto funcional comprovado.";
            if (provider == "distributedcom" && summary.EventId == 10010) return "Um componente COM demorou a responder; trate como problema real somente se houver impacto correlato.";
            if (provider == "kernel-power" && summary.EventId == 41) return "O Windows reiniciou sem desligamento limpo; a ID não distingue energia, travamento ou hardware.";
            if (provider == "kernel-power" && summary.EventId == 109) return "O kernel registrou uma transição de energia; correlacione a mensagem com outros eventos do mesmo horário.";
            if (provider == "disk") return DiskPlainText(summary.EventId);
            if (provider == "ntfs") return summary.EventId == 55 ? "O sistema de arquivos NTFS pode estar corrompido; verifique o volume e a saúde do disco." : "O volume NTFS reportou uma inconsistência; confirme o estado do sistema de arquivos e do armazenamento.";
            if (provider == "whea-logger") return WheaPlainText(summary.EventId);
            if (provider == "application error") return "Um executável falhou; a mensagem, o módulo e o aplicativo afetado devem ser verificados antes de agir.";
            if (provider == ".net runtime") return "Um aplicativo .NET lançou uma exceção não tratada; consulte a versão do runtime, o aplicativo e seus logs.";
            if (provider == "schannel") return "A negociação TLS falhou; valide data/hora, certificado, cadeia, protocolo e endpoint envolvidos.";
            if (provider == "time-service") return "O serviço de hora não conseguiu sincronizar; preserve a hierarquia de domínio e verifique o provedor de tempo.";
            if (provider == "dns-client") return "Uma consulta DNS expirou; a causa pode ser servidor DNS, rede, VPN ou domínio indisponível.";
            if (provider == "windowsupdateclient") return "Uma atualização do Windows falhou ou ficou pendente; o código e a mensagem determinam a causa.";
            if (provider == "grouppolicy" || provider == "winlogon") return "Uma política de grupo não foi aplicada; verifique SYSVOL, NETLOGON, DNS e o canal seguro.";
            if (provider == "print") return "A fila ou o serviço de impressão falhou; confirme a impressora, trabalhos pendentes, porta e driver.";
            if (provider == "volsnap") return "Um snapshot VSS não pôde ser concluído; verifique espaço, armazenamento de sombras e o backup.";
            if (provider == "perflib") return "Uma DLL ou contador de desempenho não pôde ser carregado; identifique o componente antes de corrigir.";
            if (provider == "defrag") return "Uma tarefa de otimização do disco falhou ou foi interrompida; confirme o tipo de armazenamento e a tarefa.";
            if (provider == "iscsiprt") return "Uma sessão iSCSI falhou ou ficou indisponível; valide rede, portal, serviço e o storage antes de reconectar.";
            if (provider == "storahci") return "A controladora SATA/AHCI reportou reset ou timeout; faça backup e correlacione com disco, NTFS e WHEA.";
            return "O evento indica uma ocorrência operacional; a mensagem, o assunto e o estado atual devem ser avaliados antes de agir.";
        }

        private static string EventTitle(string provider, EventSummary summary)
        {
            if (provider == "netlogon" && summary.EventId == 5719) return "Controlador de domínio não encontrado no logon";
            if (provider == "netlogon" && summary.EventId == 5805) return "Falha no canal seguro do domínio";
            if (IsServiceControlManager(provider))
            {
                var service = ExtractSafeServiceName(summary);
                return service == null ? $"Falha em serviço do Windows (evento {summary.EventId})" : $"Serviço não conseguiu iniciar: {service}";
            }
            if (provider == "bitlocker-driver") return "Operação do BitLocker pendente ou falhou";
            if (provider == "distributedcom" && summary.EventId == 10016) return "Permissão DCOM sem impacto comprovado";
            if (provider == "distributedcom" && summary.EventId == 10010) return "Componente DCOM não respondeu a tempo";
            if (provider == "kernel-power" && summary.EventId == 41) return "O computador reiniciou sem desligar corretamente";
            if (provider == "kernel-power" && summary.EventId == 109) return "Transição de energia registrada pelo kernel";
            if (provider == "disk") return DiskTitle(summary.EventId);
            if (provider == "ntfs") return summary.EventId == 55 ? "Possível corrupção do sistema de arquivos NTFS" : "Inconsistência no volume NTFS";
            if (provider == "whea-logger") return summary.EventId == 18 ? "Erro fatal de hardware" : "Erro de hardware corrigido";
            if (provider == "application error") return string.IsNullOrWhiteSpace(summary.Subject) ? "Falha de aplicativo" : "Aplicativo falhou: " + summary.Subject;
            if (provider == ".net runtime") return string.IsNullOrWhiteSpace(summary.Subject) ? ".NET Runtime falhou" : ".NET Runtime falhou: " + summary.Subject;
            if (provider == "schannel") return "Falha de negociação TLS";
            if (provider == "time-service") return "Relógio do Windows não sincronizou";
            if (provider == "dns-client") return "A consulta de DNS expirou";
            if (provider == "windowsupdateclient") return "Falha do Windows Update";
            if (provider == "grouppolicy" || provider == "winlogon") return "Política de grupo não foi aplicada";
            if (provider == "print") return "Falha na fila de impressão";
            if (provider == "volsnap") return "Falha em snapshot VSS";
            if (provider == "perflib") return "Contador de desempenho não carregou";
            if (provider == "defrag") return "Falha na otimização do disco";
            if (provider == "iscsiprt") return "Sessão iSCSI indisponível";
            if (provider == "storahci") return "Erro na controladora SATA/AHCI";
            if (!string.IsNullOrWhiteSpace(summary.Explanation) && summary.Explanation.IndexOf("agrupado para análise", StringComparison.OrdinalIgnoreCase) < 0)
                return summary.Explanation;
            return $"Erro {summary.Provider} (evento {summary.EventId})";
        }

        private static string ServicePlainText(EventSummary summary)
        {
            var service = ExtractSafeServiceName(summary);
            var name = service == null ? "um serviço" : "o serviço " + service;
            switch (summary.EventId)
            {
                case 7000: return $"{name} não conseguiu iniciar; verifique dependências e o código de erro.";
                case 7001: return $"{name} não iniciou porque um serviço dependente falhou.";
                case 7009: return $"{name} demorou além do tempo limite para iniciar.";
                case 7011: return $"{name} não respondeu a uma solicitação dentro do tempo limite.";
                case 7023: return $"{name} terminou com um erro específico do serviço.";
                case 7026: return $"Um driver ou serviço necessário não pôde ser carregado.";
                case 7031: return $"{name} terminou inesperadamente e foi reiniciado pelo Windows.";
                default: return $"{name} terminou inesperadamente.";
            }
        }

        private static string DiskTitle(int id)
        {
            switch (id)
            {
                case 7: return "Falha de leitura no disco";
                case 11: return "Erro de entrada/saída no disco";
                case 51: return "Falha de leitura/gravação no disco";
                default: return "Operação de E/S repetida pelo disco";
            }
        }

        private static string DiskPlainText(int id)
        {
            switch (id)
            {
                case 7: return "O disco reportou um setor defeituoso; faça backup e verifique a saúde do armazenamento.";
                case 11: return "O disco reportou um erro de entrada/saída; investigue cabo, controladora e unidade.";
                case 51: return "O Windows encontrou uma falha de leitura/gravação; isso pode indicar cabo, controladora ou disco falhando.";
                default: return "O disco precisou repetir uma operação; observe recorrência e faça backup antes de qualquer reparo.";
            }
        }

        private static string WheaPlainText(int id)
        {
            if (id == 18) return "O WHEA registrou um erro fatal de hardware; verifique processador, memória, temperatura, overclock e fonte.";
            if (id == 17) return "O WHEA registrou um erro corrigido no barramento PCIe; verifique GPU, SSD NVMe, slot e firmware.";
            if (id == 19) return "O WHEA registrou um erro corrigido no processador ou memória; acompanhe a frequência e teste o hardware.";
            if (id == 47) return "O WHEA registrou um erro corrigido de memória; se for recorrente, teste a RAM.";
            return "O WHEA registrou um evento de hardware; correlacione componente, mensagem e frequência.";
        }

        private static string BuildEvidence(EventSummary summary)
        {
            var message = string.IsNullOrWhiteSpace(summary.Message) ? "sem mensagem" : summary.Message.Trim();
            var subject = string.IsNullOrWhiteSpace(summary.Subject) ? "sem assunto" : summary.Subject.Trim();
            return $"Provedor: {summary.Provider}; ID: {summary.EventId}; assunto: {subject}; ocorrências: {summary.Count}; mensagem: {message}";
        }

        private static string Verification(string provider, EventSummary summary)
        {
            if (provider == "dns-client") return "Usar nslookup para o nome afetado e observar novos eventos 1014.";
            if (provider == "time-service") return "Consultar w32tm /query /status e confirmar hora sincronizada.";
            if (provider == "disk" || provider == "ntfs") return "Revisar a saída do CHKDSK e acompanhar novos eventos; persistência exige backup e investigação do hardware.";
            if (provider == "windowsupdateclient") return "Abrir o Windows Update, procurar atualizações e observar novos eventos 20/25/31.";
            if (provider == "grouppolicy") return "Executar gpresult /r ou gerar um relatório e confirmar novo logon sem 1058/1030.";
            return "Repetir a operação afetada e verificar se não surgiram novas ocorrências do mesmo assunto.";
        }

        private static string Guidance(string provider, EventSummary summary)
        {
            if (provider == "netlogon") return "Sem correção automática: orientar nltest /dsgetdc:<dominio>, nltest /sc_verify:<dominio>, DNS e horário; somente com domínio conhecido e confirmação avaliar nltest /sc_reset:<dominio>.";
            if (provider == "bitlocker-driver") return "Sem correção automática: orientar manage-bde -status C:, manage-bde -protectors -get C:, backup da chave e suporte; não suspender nem descriptografar automaticamente.";
            if (provider == "distributedcom") return "Sem correção automática: orientar ignorar DCOM 10016 sem impacto e, para 10010, reiniciar apenas o aplicativo/serviço identificado com confirmação.";
            if (provider == "kernel-power") return "Sem correção automática: orientar powercfg /requests, powercfg /energy /output \"%TEMP%\\energy-report.html\", temperaturas, fonte, drivers e dumps.";
            if (provider == "whea-logger") return "Sem correção automática: orientar firmware, drivers, temperatura, overclock, fonte, RAM e PCIe; mdsched.exe somente como diagnóstico interativo.";
            if (provider == "application error") return "Sem correção automática: orientar atualização, reparo ou reinstalação do aplicativo; usar SFC/DISM somente quando a mensagem indicar módulo do Windows.";
            if (provider == ".net runtime") return "Sem correção automática: orientar versão do aplicativo/runtime, logs e stack trace; não reinstalar runtime às cegas.";
            if (provider == "schannel") return "Sem correção automática: orientar data/hora, certificado, cadeia e endpoint; não habilitar protocolos fracos nem ignorar validação.";
            if (provider == "volsnap") return "Sem correção automática: orientar vssadmin list shadows, vssadmin list shadowstorage e espaço livre; não apagar shadows sem confirmação.";
            if (provider == "perflib") return "Sem correção automática: orientar identificar serviço/DLL no Message e corrigir pelo fornecedor; não registrar DLL às cegas.";
            if (provider == "iscsiPrt".ToLowerInvariant()) return "Sem correção automática: orientar sc.exe query MSiSCSI, rede, portal e storage; não desconectar nem relogar sessão automaticamente.";
            if (provider == "storahci") return "Sem correção automática: orientar backup, firmware/driver, cabo/porta e saúde do disco; correlacionar com disk, Ntfs e WHEA.";
            if (provider == "defrag") return "Sem correção automática: orientar defrag %SystemDrive% /A /V e revisar a tarefa; não forçar desfragmentação em SSD.";
            if (provider == "service control manager") return "Sem correção automática: orientar sc.exe query <serviço>, sc.exe qc <serviço> e dependências; só iniciar serviço identificado, existente e não crítico com confirmação.";
            if (provider == "print") return "Sem correção automática: orientar confirmar a impressora/fila; reiniciar o spooler somente com confirmação, pois trabalhos pendentes podem ser afetados.";
            if (provider == "winlogon") return "Sem correção automática: orientar verificar SYSVOL/NETLOGON, DNS, canal seguro e gpresult /r.";
            return "Sem correção automática: orientar revisar a mensagem completa, o assunto, dependências e o estado atual antes de alterar o sistema.";
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return values.Any(value => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
