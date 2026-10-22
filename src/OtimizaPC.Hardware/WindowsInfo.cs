using System;
using Microsoft.Win32;
using OtimizaPC.Hardware.Interop;

namespace OtimizaPC.Hardware
{
    /// <summary>Informações do Windows lidas do registro.</summary>
    public static class WindowsInfo
    {
        private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

        /// <summary>Ex.: "24H2 (build 26100.2161)" ou "Build 9600.21620" no Windows 8.1.</summary>
        public static string DisplayVersion
        {
            get
            {
                var display = Reg.GetValue(RegistryHive.LocalMachine, CurrentVersionKey, "DisplayVersion") as string
                              ?? Reg.GetValue(RegistryHive.LocalMachine, CurrentVersionKey, "ReleaseId") as string;
                var build = Reg.GetValue(RegistryHive.LocalMachine, CurrentVersionKey, "CurrentBuild") as string
                            ?? Reg.GetValue(RegistryHive.LocalMachine, CurrentVersionKey, "CurrentBuildNumber") as string;
                var ubr = Reg.GetValue(RegistryHive.LocalMachine, CurrentVersionKey, "UBR");
                var fullBuild = ubr != null ? $"{build}.{ubr}" : build;
                return string.IsNullOrEmpty(display) ? $"Build {fullBuild}" : $"{display} (build {fullBuild})";
            }
        }

        public static string FirmwareType
        {
            get
            {
                try
                {
                    if (NativeMethods.GetFirmwareType(out var type))
                    {
                        switch (type)
                        {
                            case 1: return "BIOS (Legacy)";
                            case 2: return "UEFI";
                        }
                    }
                }
                catch (EntryPointNotFoundException)
                {
                    // Windows anterior ao 8: API inexistente
                }
                return null;
            }
        }

        public static string SecureBoot
        {
            get
            {
                var value = Reg.GetValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
                switch (value)
                {
                    case 1: return "Ativado";
                    case 0: return "Desativado";
                    default: return "Não suportado";
                }
            }
        }

        public static bool RebootPending =>
            Reg.KeyExists(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
            || Reg.KeyExists(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
    }
}
