using System;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;

namespace OtimizaPC.Tweaks.Registry
{
    /// <summary>Um valor de registro que a otimização define.</summary>
    public sealed class RegistryChange
    {
        public RegistryChange(RegistryHive hive, string path, string name, object value, RegistryValueKind kind, object defaultValue = null)
        {
            Hive = hive;
            Path = path;
            Name = name ?? string.Empty;
            Value = value;
            Kind = kind;
            DefaultValue = defaultValue;
        }

        public RegistryHive Hive { get; }

        public string Path { get; }

        /// <summary>Nome do valor; vazio para o valor padrão da chave.</summary>
        public string Name { get; }

        public object Value { get; }

        public RegistryValueKind Kind { get; }

        /// <summary>Padrão do Windows, usado ao reverter sem registro de desfazer. Null = remover o valor.</summary>
        public object DefaultValue { get; }

        /// <summary>
        /// Ao reverter sem registro de desfazer: chave mais alta que pode ser removida se ficar vazia
        /// (quando a otimização cria uma chave inteira, como o menu de contexto clássico).
        /// </summary>
        public string DefaultDeleteKey { get; set; }

        public static RegistryChange Dword(RegistryHive hive, string path, string name, int value, int? defaultValue = null) =>
            new RegistryChange(hive, path, name, value, RegistryValueKind.DWord, defaultValue);

        public static RegistryChange String(RegistryHive hive, string path, string name, string value, string defaultValue = null) =>
            new RegistryChange(hive, path, name, value, RegistryValueKind.String, defaultValue);

        public bool IsCurrent()
        {
            using (var key = RegistryAccess.Open(Hive, Path, writable: false))
            {
                if (key == null) return false;
                var current = key.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return ValuesEqual(current, Value);
            }
        }

        internal static bool ValuesEqual(object current, object expected)
        {
            if (current == null || expected == null) return current == expected;
            switch (expected)
            {
                case int i when current is int c: return c == i;
                case long l when current is long c: return c == l;
                case string s when current is string c: return string.Equals(c, s, StringComparison.Ordinal);
                case byte[] b when current is byte[] c: return c.SequenceEqual(b);
                case string[] a when current is string[] c: return c.SequenceEqual(a);
                default: return false;
            }
        }

        public override string ToString() => $@"{Hive}\{Path}\{Name}";

        #region Serialização de valores para o registro de desfazer

        internal static string Serialize(object value, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord: return Convert.ToInt32(value).ToString(CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord: return Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture);
                case RegistryValueKind.Binary: return Convert.ToBase64String((byte[])value);
                case RegistryValueKind.MultiString: return string.Join("\0", (string[])value);
                default: return value?.ToString();
            }
        }

        internal static object Deserialize(string data, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord: return int.Parse(data, CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord: return long.Parse(data, CultureInfo.InvariantCulture);
                case RegistryValueKind.Binary: return Convert.FromBase64String(data);
                case RegistryValueKind.MultiString: return data.Length == 0 ? new string[0] : data.Split('\0');
                default: return data;
            }
        }

        #endregion
    }

    internal static class RegistryAccess
    {
        private static RegistryView View => Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;

        public static RegistryKey BaseKey(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, View);

        public static RegistryKey Open(RegistryHive hive, string path, bool writable)
        {
            using (var root = BaseKey(hive))
                return root.OpenSubKey(path, writable);
        }

        /// <summary>Primeiro nível do caminho que ainda não existe (será criado), ou null se a chave já existe.</summary>
        public static string FirstMissingKey(RegistryHive hive, string path)
        {
            using (var root = BaseKey(hive))
            {
                var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 1; i <= parts.Length; i++)
                {
                    var partial = string.Join("\\", parts.Take(i));
                    using (var key = root.OpenSubKey(partial))
                    {
                        if (key == null) return partial;
                    }
                }
                return null;
            }
        }
    }
}
