using System;
using Microsoft.Win32;

namespace OtimizaPC.Hardware.Interop
{
    /// <summary>Leitura segura do registro, sempre na visão nativa (64 bits em SO 64 bits).</summary>
    public static class Reg
    {
        private static RegistryView View => Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;

        public static RegistryKey Open(RegistryHive hive, string path)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, View))
                    return root.OpenSubKey(path);
            }
            catch
            {
                return null;
            }
        }

        public static object GetValue(RegistryHive hive, string path, string name)
        {
            try
            {
                using (var key = Open(hive, path))
                    return key?.GetValue(name);
            }
            catch
            {
                return null;
            }
        }

        public static bool KeyExists(RegistryHive hive, string path)
        {
            using (var key = Open(hive, path))
                return key != null;
        }

        public static int ValueCount(RegistryHive hive, string path)
        {
            try
            {
                using (var key = Open(hive, path))
                    return key?.ValueCount ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Converte valores DWORD/QWORD/binários do registro em número.</summary>
        public static ulong? ToULong(object value)
        {
            switch (value)
            {
                case long l: return (ulong)l;
                case int i: return (uint)i;
                case byte[] b when b.Length >= 8: return BitConverter.ToUInt64(b, 0);
                case byte[] b when b.Length >= 4: return BitConverter.ToUInt32(b, 0);
                default: return null;
            }
        }
    }
}
