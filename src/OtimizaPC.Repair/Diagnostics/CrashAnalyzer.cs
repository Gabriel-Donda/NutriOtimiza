using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using OtimizaPC.Core;

namespace OtimizaPC.Repair.Diagnostics
{
    public sealed class CrashRecord
    {
        public DateTime When { get; set; }

        /// <summary>Código de parada no formato 0x0000009F; null para desligamento sem tela azul.</summary>
        public string Code { get; set; }

        public string Name { get; set; }

        public string Explanation { get; set; }

        public string Parameters { get; set; }

        public string DumpFile { get; set; }
    }

    /// <summary>Histórico de telas azuis e desligamentos inesperados, a partir do log de eventos e dos minidumps.</summary>
    public static class CrashAnalyzer
    {
        private static readonly Regex HexPattern = new Regex(@"0x[0-9a-fA-F]{8,16}");
        private static readonly Regex DumpPathPattern = new Regex(@"[A-Za-z]:\\[^\r\n""]+?\.dmp", RegexOptions.IgnoreCase);
        private static readonly TimeSpan MatchWindow = TimeSpan.FromMinutes(15);

        public static string MinidumpFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");

        public static IReadOnlyList<CrashRecord> Analyze(int days, CancellationToken cancellationToken)
        {
            var crashes = new List<CrashRecord>();
            ReadBugCheckEvents(days, crashes, cancellationToken);
            MergeDumps(crashes);
            ReadUnexpectedShutdowns(days, crashes, cancellationToken);
            return crashes.OrderByDescending(c => c.When).ToList();
        }

        private static void ReadBugCheckEvents(int days, List<CrashRecord> crashes, CancellationToken cancellationToken)
        {
            foreach (var record in Query("*[System[(Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] or Provider[@Name='BugCheck']) and EventID=1001]]", days, cancellationToken))
            {
                using (record)
                {
                    var message = SafeMessage(record);
                    var parsed = ParseBugCheckMessage(message);
                    if (parsed.Code == null) continue;
                    crashes.Add(Build(record.TimeCreated ?? DateTime.MinValue, parsed.Code, parsed.Parameters, parsed.DumpFile));
                }
            }
        }

        /// <summary>Kernel-Power 41: desligamentos sem desligar normalmente; com código de parada quando houve tela azul.</summary>
        private static void ReadUnexpectedShutdowns(int days, List<CrashRecord> crashes, CancellationToken cancellationToken)
        {
            foreach (var record in Query("*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41]]", days, cancellationToken))
            {
                using (record)
                {
                    var when = record.TimeCreated ?? DateTime.MinValue;
                    // A tela azul já registrada pelo evento 1001 é a mesma ocorrência
                    if (crashes.Any(c => c.Code != null && Math.Abs((c.When - when).TotalMinutes) < 30)) continue;

                    long code = 0;
                    try
                    {
                        if (record.Properties.Count > 0) code = Convert.ToInt64(record.Properties[0].Value);
                    }
                    catch
                    {
                        // Formato diferente em versões antigas
                    }

                    crashes.Add(code != 0
                        ? Build(when, "0x" + code.ToString("X8"), null, null)
                        : new CrashRecord
                        {
                            When = when,
                            Name = "Desligamento inesperado",
                            Explanation = "O PC desligou ou reiniciou sem tela azul: queda de energia, botão de força, fonte ou superaquecimento.",
                        });
                }
            }
        }

        private static void MergeDumps(List<CrashRecord> crashes)
        {
            FileInfo[] dumps;
            try
            {
                var folder = new DirectoryInfo(MinidumpFolder);
                if (!folder.Exists) return;
                dumps = folder.GetFiles("*.dmp");
            }
            catch (Exception ex)
            {
                // A pasta Minidump exige administrador
                Log.Info("Minidumps inacessíveis: " + ex.Message);
                return;
            }

            foreach (var dump in dumps)
            {
                var existing = crashes.FirstOrDefault(c =>
                    string.Equals(c.DumpFile, dump.FullName, StringComparison.OrdinalIgnoreCase)
                    || (c.DumpFile == null && Math.Abs((c.When - dump.LastWriteTime).TotalMinutes) < MatchWindow.TotalMinutes));
                if (existing != null)
                {
                    existing.DumpFile = dump.FullName;
                    continue;
                }

                if (DumpHeader.TryRead(dump.FullName, out var header))
                    crashes.Add(Build(dump.LastWriteTime, Hex(header.BugCheckCode), string.Join(", ", header.Parameters.Select(p => "0x" + p.ToString("X"))), dump.FullName));
            }
        }

        private static CrashRecord Build(DateTime when, string code, string parameters, string dumpFile)
        {
            var info = BugCheckCodes.Describe(code);
            return new CrashRecord
            {
                When = when,
                Code = code,
                Name = info.Name,
                Explanation = info.Explanation,
                Parameters = parameters,
                DumpFile = dumpFile,
            };
        }

        /// <summary>Extrai código, parâmetros e arquivo de dump da mensagem do evento 1001 (qualquer idioma).</summary>
        internal static (string Code, string Parameters, string DumpFile) ParseBugCheckMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return (null, null, null);
            var hex = HexPattern.Matches(message).Cast<Match>().Select(m => m.Value).ToList();
            if (hex.Count == 0) return (null, null, null);

            var code = Hex(ulong.Parse(hex[0].Substring(2), NumberStyles.HexNumber));
            var parameters = hex.Count > 1 ? string.Join(", ", hex.Skip(1).Take(4)) : null;
            var dump = DumpPathPattern.Match(message);
            return (code, parameters, dump.Success ? dump.Value : null);
        }

        private static string Hex(ulong code) => "0x" + code.ToString("X8");

        private static IEnumerable<EventRecord> Query(string xpath, int days, CancellationToken cancellationToken)
        {
            var milliseconds = (long)TimeSpan.FromDays(days).TotalMilliseconds;
            var filter = xpath.Insert(xpath.Length - 2, $" and TimeCreated[timediff(@SystemTime) <= {milliseconds}]");
            var query = new EventLogQuery("System", PathType.LogName, filter) { ReverseDirection = true };

            EventLogReader reader;
            try
            {
                reader = new EventLogReader(query);
            }
            catch (EventLogException ex)
            {
                Log.Warn("Falha ao consultar o log de eventos: " + ex.Message);
                yield break;
            }

            using (reader)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var record = reader.ReadEvent();
                    if (record == null) yield break;
                    yield return record;
                }
            }
        }

        private static string SafeMessage(EventRecord record)
        {
            try
            {
                return record.FormatDescription();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Cabeçalho de arquivo de despejo do kernel (minidump/MEMORY.DMP).</summary>
    public sealed class DumpHeader
    {
        public bool Is64Bit { get; set; }

        public ulong BugCheckCode { get; set; }

        public ulong[] Parameters { get; set; }

        public static bool TryRead(string path, out DumpHeader header)
        {
            header = null;
            try
            {
                var bytes = new byte[0x60];
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Read(bytes, 0, bytes.Length) < bytes.Length) return false;
                }
                return TryParse(bytes, out header);
            }
            catch (Exception ex)
            {
                Log.Warn($"Não foi possível ler o dump {path}: {ex.Message}");
                return false;
            }
        }

        /// <summary>DUMP_HEADER64 ("PAGEDU64"): código em 0x38, parâmetros em 0x40. DUMP_HEADER32 ("PAGEDUMP"): código em 0x28.</summary>
        internal static bool TryParse(byte[] bytes, out DumpHeader header)
        {
            header = null;
            if (bytes.Length < 0x60) return false;
            var signature = System.Text.Encoding.ASCII.GetString(bytes, 0, 8);

            if (signature == "PAGEDU64")
            {
                header = new DumpHeader
                {
                    Is64Bit = true,
                    BugCheckCode = BitConverter.ToUInt32(bytes, 0x38),
                    Parameters = Enumerable.Range(0, 4).Select(i => BitConverter.ToUInt64(bytes, 0x40 + i * 8)).ToArray(),
                };
                return true;
            }

            if (signature == "PAGEDUMP")
            {
                header = new DumpHeader
                {
                    BugCheckCode = BitConverter.ToUInt32(bytes, 0x28),
                    Parameters = Enumerable.Range(0, 4).Select(i => (ulong)BitConverter.ToUInt32(bytes, 0x2C + i * 4)).ToArray(),
                };
                return true;
            }
            return false;
        }
    }
}
