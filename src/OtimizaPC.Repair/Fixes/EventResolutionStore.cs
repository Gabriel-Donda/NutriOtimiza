using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.ServiceProcess;
using System.Text;
using OtimizaPC.Repair.Diagnostics;

namespace OtimizaPC.Repair.Fixes
{
    [DataContract]
    public sealed class EventResolution
    {
        [DataMember] public string Key { get; set; }
        [DataMember] public DateTime FixedAt { get; set; }
        [DataMember] public bool Verified { get; set; }
        [DataMember] public string Result { get; set; }
    }

    /// <summary>Persiste somente o estado de apresentação; o histórico do Windows permanece intacto.</summary>
    public static class EventResolutionStore
    {
        private static readonly object Gate = new object();

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NutriOtimiza", "fixed-events.json");

        public static string KeyFor(EventSummary summary) => summary == null ? string.Empty
            : (summary.Provider ?? string.Empty).Trim().ToLowerInvariant() + "|" + summary.EventId + "|" + (summary.Subject ?? string.Empty).Trim().ToLowerInvariant();

        public static IReadOnlyList<EventResolution> Load() => Load(FilePath);

        public static IReadOnlyList<EventResolution> Load(string filePath)
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(filePath)) return new List<EventResolution>();
                    using (var stream = File.OpenRead(filePath))
                        return (List<EventResolution>)new DataContractJsonSerializer(typeof(List<EventResolution>)).ReadObject(stream)
                            ?? new List<EventResolution>();
                }
                catch (Exception) { return new List<EventResolution>(); }
            }
        }

        public static EventResolution Get(EventSummary summary)
        {
            var key = KeyFor(summary);
            return Load().Where(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.FixedAt).FirstOrDefault();
        }

        public static bool IsResolved(EventSummary summary)
        {
            var item = Get(summary);
            return IsResolved(summary, item);
        }

        public static bool IsResolved(EventSummary summary, EventResolution item) =>
            item != null && item.Verified && summary != null && summary.Last <= item.FixedAt;

        public static EventResolution Record(EventSummary summary, DateTime fixedAt, bool verified, string result) =>
            Record(FilePath, summary, fixedAt, verified, result);

        public static EventResolution Record(string filePath, EventSummary summary, DateTime fixedAt, bool verified, string result)
        {
            var item = new EventResolution { Key = KeyFor(summary), FixedAt = fixedAt, Verified = verified, Result = result };
            lock (Gate)
            {
                var list = Load(filePath).Where(x => !string.Equals(x.Key, item.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                list.Add(item);
                var directory = Path.GetDirectoryName(filePath);
                Directory.CreateDirectory(directory);
                var temporary = filePath + ".tmp";
                using (var stream = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(List<EventResolution>)).WriteObject(stream, list);
                if (File.Exists(filePath)) File.Replace(temporary, filePath, null);
                else File.Move(temporary, filePath);
            }
            return item;
        }
    }

    public sealed class VerificationStep
    {
        public string Label { get; set; }
        public bool Success { get; set; }
        public string Symbol => Success ? "CheckmarkCircle24" : "DismissCircle24";
    }

    public sealed class EventFixVerification
    {
        public bool Success => Steps.Count > 0 && Steps.All(x => x.Success);
        public IReadOnlyList<VerificationStep> Steps { get; set; } = new List<VerificationStep>();

        public static EventFixVerification Verify(EventSummary summary, string fixId, bool commandSucceeded)
        {
            var steps = new List<VerificationStep>();
            if (fixId == "windows-update-reset")
            {
                foreach (var name in new[] { "wuauserv", "bits", "cryptsvc" })
                    steps.Add(ServiceRunning(name));
                var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                steps.Add(new VerificationStep { Label = "Pastas antigas renomeadas", Success = Directory.Exists(windows) && Directory.GetDirectories(windows, "SoftwareDistribution.bak-*").Length > 0 && Directory.GetDirectories(Environment.SystemDirectory, "catroot2.bak-*").Length > 0 });
            }
            else if (fixId.StartsWith("service-start-", StringComparison.OrdinalIgnoreCase))
                steps.Add(ServiceRunning(fixId.Substring("service-start-".Length)));
            else if (fixId == "dns-1014-flush")
                steps.Add(new VerificationStep { Label = "Assunto resolve no DNS", Success = Resolve(summary == null ? null : summary.Subject) });
            else if (fixId.StartsWith("time-service-", StringComparison.OrdinalIgnoreCase))
                steps.Add(new VerificationStep { Label = "Relógio sincronizado", Success = commandSucceeded });
            else
                steps.Add(new VerificationStep { Label = "Código de saída sem erro", Success = commandSucceeded });
            return new EventFixVerification { Steps = steps };
        }

        private static VerificationStep ServiceRunning(string name)
        {
            var ok = false;
            try { using (var service = new ServiceController(name)) ok = service.Status == ServiceControllerStatus.Running; }
            catch (Exception) { }
            return new VerificationStep { Label = "Serviço " + name + " em execução", Success = ok };
        }

        private static bool Resolve(string subject)
        {
            try { System.Net.Dns.GetHostEntry(string.IsNullOrWhiteSpace(subject) ? "localhost" : subject.Trim()); return true; }
            catch (Exception) { return false; }
        }
    }
}
