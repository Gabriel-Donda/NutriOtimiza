using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Threading;
using OtimizaPC.Core;

namespace OtimizaPC.Repair.Diagnostics
{
    public enum EventImportance
    {
        /// <summary>Indica problema de hardware, disco ou estabilidade.</summary>
        High,
        Normal,

        /// <summary>Erro conhecido e inofensivo (ex.: DCOM 10016).</summary>
        Benign,
    }

    /// <summary>Grupo de eventos de erro iguais (mesma origem e ID).</summary>
    public sealed class EventSummary
    {
        public string Log { get; set; }

        public string Provider { get; set; }

        public int EventId { get; set; }

        /// <summary>Detalhe que diferencia o grupo (ex.: nome do programa que travou).</summary>
        public string Subject { get; set; }

        public bool IsCritical { get; set; }

        public int Count { get; set; }

        public DateTime First { get; set; }

        public DateTime Last { get; set; }

        public string Message { get; set; }

        public string Explanation { get; set; }

        public EventImportance Importance { get; set; } = EventImportance.Normal;
    }

    /// <summary>Resume os erros e eventos críticos dos logs Sistema e Aplicativo dos últimos dias.</summary>
    public static class EventLogAnalyzer
    {
        private const int MaxEventsPerLog = 5000;

        private static readonly Dictionary<string, (string Text, EventImportance Importance)> Known =
            new Dictionary<string, (string, EventImportance)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Microsoft-Windows-Kernel-Power|41"] = ("O PC desligou sem um desligamento normal: queda de energia, travamento ou tela azul.", EventImportance.High),
                ["EventLog|6008"] = ("O desligamento anterior foi inesperado.", EventImportance.High),
                ["Microsoft-Windows-WER-SystemErrorReporting|1001"] = ("O Windows travou com tela azul. Veja a aba Telas azuis.", EventImportance.High),
                ["BugCheck|1001"] = ("O Windows travou com tela azul. Veja a aba Telas azuis.", EventImportance.High),
                ["disk|7"] = ("Setor defeituoso no disco. Faça backup e verifique a saúde (SMART) na tela Hardware.", EventImportance.High),
                ["disk|51"] = ("Erro de leitura/gravação no disco: cabo, controladora ou disco falhando.", EventImportance.High),
                ["disk|153"] = ("O disco precisou repetir operações: sinal de disco ou cabo com problema.", EventImportance.High),
                ["Ntfs|55"] = ("Estrutura do sistema de arquivos corrompida. Rode a verificação de disco.", EventImportance.High),
                ["Microsoft-Windows-Ntfs|55"] = ("Estrutura do sistema de arquivos corrompida. Rode a verificação de disco.", EventImportance.High),
                ["Microsoft-Windows-WHEA-Logger|1"] = ("Erro de hardware (processador, memória ou barramento). Pode ser overclock instável ou peça com defeito.", EventImportance.High),
                ["Microsoft-Windows-WHEA-Logger|17"] = ("Erro de hardware corrigido no barramento PCIe (placa de vídeo, SSD NVMe ou slot).", EventImportance.High),
                ["Microsoft-Windows-WHEA-Logger|18"] = ("Erro fatal de hardware no processador. Verifique temperatura, overclock e fonte.", EventImportance.High),
                ["Microsoft-Windows-WHEA-Logger|19"] = ("Erro de hardware corrigido no processador ou memória.", EventImportance.High),
                ["Microsoft-Windows-WHEA-Logger|47"] = ("Erro de memória RAM corrigido. Se for frequente, teste a memória.", EventImportance.High),
                ["Display|4101"] = ("O driver de vídeo parou de responder e foi reiniciado. Atualize o driver ou verifique a temperatura da GPU.", EventImportance.High),
                ["nvlddmkm|13"] = ("Erro no driver de vídeo NVIDIA.", EventImportance.High),
                ["nvlddmkm|14"] = ("Erro no driver de vídeo NVIDIA.", EventImportance.High),
                ["nvlddmkm|153"] = ("O driver de vídeo NVIDIA travou e foi reiniciado.", EventImportance.High),
                ["volmgr|161"] = ("O Windows não conseguiu gravar o arquivo de despejo da tela azul.", EventImportance.Normal),
                ["Microsoft-Windows-Kernel-PnP|219"] = ("Um driver não foi carregado para um dispositivo.", EventImportance.Normal),
                ["Service Control Manager|7000"] = ("Um serviço não conseguiu iniciar.", EventImportance.Normal),
                ["Service Control Manager|7001"] = ("Um serviço não iniciou porque depende de outro que falhou.", EventImportance.Normal),
                ["Service Control Manager|7009"] = ("Um serviço demorou demais para iniciar.", EventImportance.Normal),
                ["Service Control Manager|7023"] = ("Um serviço parou com erro.", EventImportance.Normal),
                ["Service Control Manager|7031"] = ("Um serviço parou inesperadamente e foi reiniciado.", EventImportance.Normal),
                ["Service Control Manager|7034"] = ("Um serviço parou inesperadamente.", EventImportance.Normal),
                ["Application Error|1000"] = ("Um programa travou e foi fechado.", EventImportance.Normal),
                ["Application Hang|1002"] = ("Um programa parou de responder e foi fechado.", EventImportance.Normal),
                [".NET Runtime|1026"] = ("Um programa .NET travou.", EventImportance.Normal),
                ["Microsoft-Windows-DistributedCOM|10016"] = ("Aviso de permissão DCOM comum no Windows. É inofensivo e pode ser ignorado.", EventImportance.Benign),
                ["Microsoft-Windows-DistributedCOM|10010"] = ("Um componente do Windows demorou a responder. Geralmente inofensivo.", EventImportance.Benign),
            };

        public static IReadOnlyList<EventSummary> Analyze(int days, CancellationToken cancellationToken)
        {
            var groups = new Dictionary<string, EventSummary>(StringComparer.OrdinalIgnoreCase);
            foreach (var log in new[] { "System", "Application" })
                Read(log, days, groups, cancellationToken);

            return groups.Values
                .OrderBy(g => g.Importance)
                .ThenByDescending(g => g.IsCritical)
                .ThenByDescending(g => g.Count)
                .ToList();
        }

        private static void Read(string log, int days, Dictionary<string, EventSummary> groups, CancellationToken cancellationToken)
        {
            var milliseconds = (long)TimeSpan.FromDays(days).TotalMilliseconds;
            var query = new EventLogQuery(log, PathType.LogName,
                $"*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {milliseconds}]]]")
            {
                ReverseDirection = true, // mais recentes primeiro: a mensagem de cada grupo é a da última ocorrência
            };

            try
            {
                using (var reader = new EventLogReader(query))
                {
                    for (var i = 0; i < MaxEventsPerLog; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using (var record = reader.ReadEvent())
                        {
                            if (record == null) break;
                            Add(log, record, groups);
                        }
                    }
                }
            }
            catch (EventLogException ex)
            {
                Log.Warn($"Falha ao ler o log {log}: {ex.Message}");
            }
        }

        private static void Add(string log, EventRecord record, Dictionary<string, EventSummary> groups)
        {
            var provider = record.ProviderName ?? "?";
            var subject = Subject(provider, record.Id, record);
            var key = $"{log}|{provider}|{record.Id}|{subject}";
            var when = record.TimeCreated ?? DateTime.MinValue;

            if (groups.TryGetValue(key, out var group))
            {
                group.Count++;
                if (when < group.First) group.First = when;
                return;
            }

            Known.TryGetValue($"{provider}|{record.Id}", out var known);
            groups[key] = new EventSummary
            {
                Log = log == "System" ? "Sistema" : "Aplicativo",
                Provider = provider,
                EventId = record.Id,
                Subject = subject,
                IsCritical = record.Level == 1,
                Count = 1,
                First = when,
                Last = when,
                Message = SafeMessage(record),
                Explanation = known.Text,
                Importance = known.Text != null ? known.Importance : EventImportance.Normal,
            };
        }

        /// <summary>Para travamentos de programas, separa os grupos por nome do executável.</summary>
        internal static string Subject(string provider, int id, EventRecord record)
        {
            if (!((provider == "Application Error" && id == 1000) || (provider == "Application Hang" && id == 1002))) return null;
            try
            {
                return record.Properties.Count > 0 ? record.Properties[0].Value?.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeMessage(EventRecord record)
        {
            try
            {
                var message = record.FormatDescription();
                if (string.IsNullOrWhiteSpace(message)) return null;
                message = message.Trim();
                return message.Length > 600 ? message.Substring(0, 600) + "…" : message;
            }
            catch
            {
                return null;
            }
        }
    }
}
