using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using OtimizaPC.Core;

namespace OtimizaPC.Tweaks
{
    /// <summary>Valor original de um item do registro antes de ser alterado.</summary>
    [DataContract]
    public sealed class RegistryBackup
    {
        [DataMember] public string Hive { get; set; }

        [DataMember] public string Path { get; set; }

        [DataMember] public string Name { get; set; }

        [DataMember] public bool Existed { get; set; }

        [DataMember] public string Kind { get; set; }

        [DataMember] public string Data { get; set; }

        /// <summary>Chave mais alta criada pelo app (não existia antes); removida inteira ao reverter.</summary>
        [DataMember] public string CreatedKey { get; set; }
    }

    [DataContract]
    public sealed class UndoRecord
    {
        public UndoRecord(string tweakId)
        {
            TweakId = tweakId;
        }

        [DataMember] public string TweakId { get; set; }

        [DataMember] public DateTime? AppliedAt { get; set; }

        [DataMember] public List<RegistryBackup> Registry { get; set; } = new List<RegistryBackup>();

        [DataMember] public Dictionary<string, string> Extra { get; set; } = new Dictionary<string, string>();

        public bool HasRegistryBackup(string hive, string path, string name) =>
            Registry.Any(b => b.Hive == hive
                              && string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Arquivo JSON com o estado original de cada otimização aplicada.</summary>
    public sealed class UndoStore
    {
        private readonly object _sync = new object();
        private readonly string _filePath;
        private Dictionary<string, UndoRecord> _records;

        public UndoStore(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(AppInfo.DataDirectory, "tweaks-undo.json");
        }

        public UndoRecord Get(string tweakId)
        {
            lock (_sync)
                return Load().TryGetValue(tweakId, out var record) ? record : null;
        }

        public UndoRecord GetOrCreate(string tweakId)
        {
            lock (_sync)
            {
                var records = Load();
                if (!records.TryGetValue(tweakId, out var record))
                {
                    record = new UndoRecord(tweakId);
                    records[tweakId] = record;
                }
                return record;
            }
        }

        public void Save(UndoRecord record)
        {
            lock (_sync)
            {
                Load()[record.TweakId] = record;
                Persist();
            }
        }

        public void Remove(string tweakId)
        {
            lock (_sync)
            {
                if (Load().Remove(tweakId)) Persist();
            }
        }

        private Dictionary<string, UndoRecord> Load()
        {
            if (_records != null) return _records;
            _records = new Dictionary<string, UndoRecord>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(_filePath)) return _records;

            try
            {
                using (var stream = File.OpenRead(_filePath))
                {
                    var list = (List<UndoRecord>)Serializer().ReadObject(stream);
                    foreach (var record in list) _records[record.TweakId] = record;
                }
            }
            catch (Exception ex)
            {
                // Arquivo corrompido: preserva uma cópia para análise e começa do zero
                Log.Error("Registro de desfazer ilegível; uma cópia foi preservada", ex);
                try
                {
                    File.Copy(_filePath, _filePath + ".corrompido-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
                }
                catch
                {
                    // Melhor esforço
                }
            }
            return _records;
        }

        private void Persist()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
            var temp = _filePath + ".tmp";
            using (var stream = File.Create(temp))
                Serializer().WriteObject(stream, _records.Values.ToList());

            // Troca atômica: um travamento no meio da gravação não corrompe o arquivo existente
            if (File.Exists(_filePath)) File.Replace(temp, _filePath, null);
            else File.Move(temp, _filePath);
        }

        private static DataContractJsonSerializer Serializer() =>
            new DataContractJsonSerializer(typeof(List<UndoRecord>), new DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true,
                DateTimeFormat = new DateTimeFormat("o"),
            });
    }
}
