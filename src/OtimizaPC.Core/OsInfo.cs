using System;
using Microsoft.Win32;

namespace OtimizaPC.Core
{
    /// <summary>Versão do Windows em execução (lida do registro, que não sofre com a "mentira" de compatibilidade da API).</summary>
    public static class OsInfo
    {
        public const int Windows81 = 9600;
        public const int Windows10 = 10240;
        public const int Windows10_1703 = 15063;
        public const int Windows10_1803 = 17134;
        public const int Windows10_2004 = 19041;
        public const int Windows11 = 22000;
        public const int Windows11_22H2 = 22621;
        public const int Windows11_24H2 = 26100;

        private static readonly Lazy<int> BuildValue = new Lazy<int>(ReadBuild);
        private static readonly Lazy<bool> ServerValue = new Lazy<bool>(ReadIsServer);

        public static int Build => BuildValue.Value;

        public static bool IsServer => ServerValue.Value;

        public static bool IsWindows11 => Build >= Windows11;

        private static int ReadBuild()
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default))
                using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var text = key?.GetValue("CurrentBuildNumber") as string ?? key?.GetValue("CurrentBuild") as string;
                    if (int.TryParse(text, out var build)) return build;
                }
            }
            catch
            {
                // Cai no valor da API abaixo
            }
            return Environment.OSVersion.Version.Build;
        }

        private static bool ReadIsServer()
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default))
                using (var key = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions"))
                {
                    var type = key?.GetValue("ProductType") as string;
                    return type != null && !type.Equals("WinNT", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
