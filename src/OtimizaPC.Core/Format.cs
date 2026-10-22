using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OtimizaPC.Core
{
    /// <summary>Formatação de valores para exibição (pt-BR conforme a cultura do sistema).</summary>
    public static class Format
    {
        private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

        public static string Bytes(double bytes, int decimals = 1)
        {
            if (bytes < 0 || double.IsNaN(bytes)) bytes = 0;
            var unit = 0;
            while (bytes >= 1024 && unit < ByteUnits.Length - 1)
            {
                bytes /= 1024;
                unit++;
            }
            var format = unit == 0 ? "N0" : "N" + decimals;
            return bytes.ToString(format, CultureInfo.CurrentCulture) + " " + ByteUnits[unit];
        }

        public static string Rate(double bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

        public static string Percent(double value, int decimals = 0) =>
            value.ToString("N" + decimals, CultureInfo.CurrentCulture) + "%";

        public static string Frequency(double mhz) =>
            mhz >= 1000
                ? (mhz / 1000).ToString("N2", CultureInfo.CurrentCulture) + " GHz"
                : mhz.ToString("N0", CultureInfo.CurrentCulture) + " MHz";

        public static string LinkSpeed(long bitsPerSecond)
        {
            if (bitsPerSecond >= 1_000_000_000) return (bitsPerSecond / 1_000_000_000d).ToString("0.#", CultureInfo.CurrentCulture) + " Gbps";
            if (bitsPerSecond >= 1_000_000) return (bitsPerSecond / 1_000_000d).ToString("0.#", CultureInfo.CurrentCulture) + " Mbps";
            return (bitsPerSecond / 1000d).ToString("0.#", CultureInfo.CurrentCulture) + " Kbps";
        }

        public static string Duration(TimeSpan time)
        {
            if (time.TotalMinutes < 1) return "menos de 1 min";
            var parts = new List<string>();
            if (time.Days > 0) parts.Add(time.Days + (time.Days == 1 ? " dia" : " dias"));
            if (time.Hours > 0) parts.Add(time.Hours + " h");
            if (time.Days == 0 && time.Minutes > 0) parts.Add(time.Minutes + " min");
            return string.Join(" ", parts);
        }

        public static string Date(DateTime? date, bool withTime = false) =>
            date?.ToString(withTime ? "dd/MM/yyyy HH:mm" : "dd/MM/yyyy", CultureInfo.CurrentCulture);

        /// <summary>Junta as partes não vazias com o separador informado.</summary>
        public static string Join(string separator, params string[] parts) =>
            string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
    }
}
