using System;
using System.Collections.Generic;
using OtimizaPC.Core;
using OtimizaPC.Core.Models;

namespace OtimizaPC.Hardware
{
    /// <summary>Dados brutos do resumo do painel (independentes de WMI para permitir testes).</summary>
    public sealed class SummaryData
    {
        public string Manufacturer { get; set; }

        public string Model { get; set; }

        public string OsCaption { get; set; }

        public string OsVersion { get; set; }

        public string BiosVendor { get; set; }

        public string BiosVersion { get; set; }

        public string BoardManufacturer { get; set; }

        public string BoardProduct { get; set; }

        public string CpuName { get; set; }

        public int? Cores { get; set; }

        public int? Threads { get; set; }

        public ulong? MemoryTotal { get; set; }

        public int? MemorySticks { get; set; }

        public int? MemorySlots { get; set; }

        public string Gpu { get; set; }

        public string Disks { get; set; }

        public string Network { get; set; }

        public string Resolution { get; set; }

        public string Battery { get; set; }

        public DateTime? LastBoot { get; set; }
    }

    /// <summary>Monta o "Resumo do computador" com todos os rótulos sempre presentes.</summary>
    public static class DashboardSummary
    {
        public const string Unavailable = "Indisponível";

        public static List<InfoEntry> Build(SummaryData data)
        {
            data = data ?? new SummaryData();
            var list = new List<InfoEntry>();

            Add(list, "Computador", Format.Join(" ", data.Manufacturer, data.Model));
            Add(list, "Sistema", Format.Join(" — ", data.OsCaption, data.OsVersion));
            Add(list, "BIOS", Format.Join(" ", data.BiosVendor, data.BiosVersion));
            Add(list, "Placa-mãe", Format.Join(" ", data.BoardManufacturer, data.BoardProduct));
            Add(list, "Processador", data.CpuName);
            Add(list, "Núcleos / threads", CoresAndThreads(data.Cores, data.Threads));
            Add(list, "Memória RAM", Memory(data));
            Add(list, "Vídeo", data.Gpu);
            Add(list, "Discos", data.Disks);
            Add(list, "Rede", data.Network);
            Add(list, "Resolução", data.Resolution);
            Add(list, "Bateria", data.Battery);
            Add(list, "Último boot", Format.Date(data.LastBoot, withTime: true));

            return list;
        }

        private static string CoresAndThreads(int? cores, int? threads)
        {
            var hasCores = cores > 0;
            var hasThreads = threads > 0;
            if (hasCores && hasThreads) return $"{cores} núcleos / {threads} threads";
            if (hasCores) return $"{cores} núcleos";
            if (hasThreads) return $"{threads} threads";
            return null;
        }

        private static string Memory(SummaryData data)
        {
            if (data.MemoryTotal == null || data.MemoryTotal == 0) return null;

            var text = Format.Bytes(data.MemoryTotal.Value);
            if (data.MemorySlots > 0)
            {
                var sticks = Math.Max(0, data.MemorySticks ?? 0);
                text += $" — {sticks} de {data.MemorySlots} slots";
            }
            return text;
        }

        private static void Add(List<InfoEntry> list, string label, string value) =>
            list.Add(new InfoEntry(label, string.IsNullOrWhiteSpace(value) ? Unavailable : value.Trim()));
    }
}
