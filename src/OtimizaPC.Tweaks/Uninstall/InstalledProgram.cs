using System;
using Microsoft.Win32;

namespace OtimizaPC.Tweaks.Uninstall
{
    public sealed class InstalledProgram
    {
        public string DisplayName { get; internal set; }
        public string DisplayVersion { get; internal set; }
        public string Publisher { get; internal set; }
        public DateTime? InstallDate { get; internal set; }
        public long? EstimatedSizeKb { get; internal set; }
        public string InstallLocation { get; internal set; }
        public string DisplayIcon { get; internal set; }
        public string UninstallString { get; internal set; }
        public string QuietUninstallString { get; internal set; }
        public RegistryHive Hive { get; internal set; }
        public RegistryView View { get; internal set; }
        public string SubKeyName { get; internal set; }
        public bool IsMsi { get; internal set; }
    }
}
