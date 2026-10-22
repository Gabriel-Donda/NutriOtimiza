using System;
using System.Collections.Generic;
using System.Management;
using OtimizaPC.Core;

namespace OtimizaPC.Hardware.Interop
{
    /// <summary>Consultas WMI tolerantes a falha: nunca lançam exceção, só registram no log.</summary>
    public static class Wmi
    {
        public const string Cimv2 = @"root\cimv2";
        public const string RootWmi = @"root\wmi";
        public const string Storage = @"root\Microsoft\Windows\Storage";
        public const string Tpm = @"root\cimv2\Security\MicrosoftTpm";

        public static List<WmiRow> Query(string wql, string scope = Cimv2)
        {
            var rows = new List<WmiRow>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(scope, wql))
                using (var results = searcher.Get())
                {
                    foreach (ManagementBaseObject obj in results)
                    {
                        using (obj)
                            rows.Add(new WmiRow(obj));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"WMI falhou ({scope}: {wql}): {ex.Message}");
            }
            return rows;
        }

        /// <summary>Primeira linha do resultado, ou uma linha vazia (todas as propriedades nulas).</summary>
        public static WmiRow First(string wql, string scope = Cimv2)
        {
            var rows = Query(wql, scope);
            return rows.Count > 0 ? rows[0] : WmiRow.Empty;
        }
    }

    /// <summary>Cópia das propriedades de um objeto WMI, com leitura segura por tipo.</summary>
    public sealed class WmiRow
    {
        public static readonly WmiRow Empty = new WmiRow();

        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private WmiRow()
        {
        }

        internal WmiRow(ManagementBaseObject obj)
        {
            foreach (var property in obj.Properties)
                _values[property.Name] = property.Value;
        }

        public bool IsEmpty => _values.Count == 0;

        public object this[string name] => _values.TryGetValue(name, out var value) ? value : null;

        public string Str(string name)
        {
            var value = this[name];
            if (value == null) return null;
            var text = value.ToString().Trim();
            return text.Length == 0 ? null : text;
        }

        public ulong? ULong(string name)
        {
            try
            {
                var value = this[name];
                return value == null ? (ulong?)null : Convert.ToUInt64(value);
            }
            catch
            {
                return null;
            }
        }

        public int? Int(string name)
        {
            try
            {
                var value = this[name];
                return value == null ? (int?)null : Convert.ToInt32(value);
            }
            catch
            {
                return null;
            }
        }

        public bool? Bool(string name) => this[name] is bool b ? b : (bool?)null;

        public DateTime? Date(string name)
        {
            var text = Str(name);
            if (text == null) return null;
            try
            {
                return ManagementDateTimeConverter.ToDateTime(text);
            }
            catch
            {
                return null;
            }
        }
    }
}
