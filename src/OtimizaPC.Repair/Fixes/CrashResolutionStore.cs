using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using OtimizaPC.Repair.Diagnostics;

namespace OtimizaPC.Repair.Fixes
{
    /// <summary>
    /// Entrada de resolução para uma tela azul, persistida em JSON.
    /// A chave é o stop code normalizado (ex.: "0x000000EF"), de forma que
    /// corrigir UMA ocorrência resolve automaticamente TODAS as outras com o mesmo código.
    /// </summary>
    [DataContract]
    public sealed class CrashResolution
    {
        /// <summary>Stop code normalizado, ex.: "0x000000EF".</summary>
        [DataMember] public string StopCode { get; set; }

        /// <summary>Momento em que a correção foi aplicada.</summary>
        [DataMember] public DateTime FixedAt { get; set; }

        /// <summary>True quando a correção foi concluída com sucesso e verificada.</summary>
        [DataMember] public bool Verified { get; set; }

        /// <summary>Resumo do resultado devolvido pela ferramenta de reparo.</summary>
        [DataMember] public string Result { get; set; }

        /// <summary>Id do <see cref="CrashFix"/> executado.</summary>
        [DataMember] public string FixId { get; set; }
    }

    /// <summary>
    /// Persiste o estado de resolução de telas azuis em
    /// <c>%LocalAppData%\NutriOtimiza\fixed-crashes.json</c>.
    ///
    /// <para>
    /// <b>Chave de agrupamento:</b> o stop code normalizado (ex. "0x000000EF").
    /// Corrigir qualquer ocorrência com aquele código marca todas as outras como resolvidas,
    /// porque a causa-raiz é a mesma — exatamente o comportamento pedido pelo usuário.
    /// </para>
    ///
    /// <para>
    /// O histórico de eventos do Windows não é alterado; apenas o estado de apresentação é
    /// salvo aqui, tal como no <see cref="EventResolutionStore"/>.
    /// </para>
    /// </summary>
    public static class CrashResolutionStore
    {
        private static readonly object Gate = new object();

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NutriOtimiza", "fixed-crashes.json");

        // ── Chave ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Devolve a chave de agrupamento para um <see cref="CrashRecord"/>.
        /// A chave é o stop code normalizado; records sem código (desligamentos inesperados)
        /// ficam sem resolução automática (retorna string vazia).
        /// </summary>
        public static string KeyFor(CrashRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Code)) return string.Empty;
            return CrashFixCatalog.Normalize(record.Code);
        }

        /// <summary>Sobrecarga que aceita diretamente o stop code em texto.</summary>
        public static string KeyFor(string stopCode) =>
            string.IsNullOrWhiteSpace(stopCode) ? string.Empty : CrashFixCatalog.Normalize(stopCode);

        // ── Leitura ──────────────────────────────────────────────────────────────

        public static IReadOnlyList<CrashResolution> Load() => Load(FilePath);

        public static IReadOnlyList<CrashResolution> Load(string filePath)
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(filePath)) return new List<CrashResolution>();
                    using (var stream = File.OpenRead(filePath))
                        return (List<CrashResolution>)
                            new DataContractJsonSerializer(typeof(List<CrashResolution>)).ReadObject(stream)
                            ?? new List<CrashResolution>();
                }
                catch (Exception) { return new List<CrashResolution>(); }
            }
        }

        /// <summary>
        /// Devolve a resolução mais recente para o stop code do record, ou null se não houver.
        /// </summary>
        public static CrashResolution Get(CrashRecord record)
        {
            var key = KeyFor(record);
            if (string.IsNullOrEmpty(key)) return null;
            return Load()
                .Where(x => string.Equals(x.StopCode, key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.FixedAt)
                .FirstOrDefault();
        }

        /// <summary>Sobrecarga que aceita o stop code diretamente.</summary>
        public static CrashResolution Get(string stopCode)
        {
            var key = KeyFor(stopCode);
            if (string.IsNullOrEmpty(key)) return null;
            return Load()
                .Where(x => string.Equals(x.StopCode, key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.FixedAt)
                .FirstOrDefault();
        }

        // ── Verificação ──────────────────────────────────────────────────────────

        /// <summary>
        /// Retorna true se o stop code do record foi corrigido com verificação positiva.
        /// Como a chave é o stop code, qualquer ocorrência do mesmo código é considerada resolvida.
        /// </summary>
        public static bool IsResolved(CrashRecord record)
        {
            var item = Get(record);
            return IsResolved(record, item);
        }

        public static bool IsResolved(CrashRecord record, CrashResolution item) =>
            item != null && item.Verified && record != null && !string.IsNullOrEmpty(record.Code);

        /// <summary>Sobrecarga que aceita o stop code diretamente.</summary>
        public static bool IsResolved(string stopCode)
        {
            if (string.IsNullOrWhiteSpace(stopCode)) return false;
            var item = Get(stopCode);
            return item != null && item.Verified;
        }

        // ── Escrita ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Registra a resolução de um stop code. Substituirá qualquer entrada anterior com o mesmo código.
        /// Como a chave é o stop code, isso resolve automaticamente TODAS as ocorrências daquele código.
        /// </summary>
        public static CrashResolution Record(CrashRecord record, string fixId, DateTime fixedAt, bool verified, string result) =>
            Record(FilePath, KeyFor(record), fixId, fixedAt, verified, result);

        /// <summary>Sobrecarga que aceita o stop code diretamente.</summary>
        public static CrashResolution Record(string stopCode, string fixId, DateTime fixedAt, bool verified, string result) =>
            Record(FilePath, KeyFor(stopCode), fixId, fixedAt, verified, result);

        public static CrashResolution Record(string filePath, string stopCodeKey, string fixId, DateTime fixedAt, bool verified, string result)
        {
            if (string.IsNullOrEmpty(stopCodeKey))
                throw new ArgumentException("O stop code não pode ser vazio.", nameof(stopCodeKey));

            var item = new CrashResolution
            {
                StopCode = stopCodeKey,
                FixId = fixId,
                FixedAt = fixedAt,
                Verified = verified,
                Result = result,
            };

            lock (Gate)
            {
                // Remove entradas anteriores do mesmo stop code e adiciona a nova
                var list = Load(filePath)
                    .Where(x => !string.Equals(x.StopCode, stopCodeKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                list.Add(item);

                var directory = Path.GetDirectoryName(filePath);
                Directory.CreateDirectory(directory);
                var temporary = filePath + ".tmp";
                using (var stream = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(List<CrashResolution>)).WriteObject(stream, list);
                if (File.Exists(filePath)) File.Replace(temporary, filePath, null);
                else File.Move(temporary, filePath);
            }

            return item;
        }
    }
}
