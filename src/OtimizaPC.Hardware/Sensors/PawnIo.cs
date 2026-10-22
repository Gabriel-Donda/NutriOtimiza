using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using OtimizaPC.Hardware.Interop;

namespace OtimizaPC.Hardware.Sensors
{
    /// <summary>
    /// Driver PawnIO, exigido pela LibreHardwareMonitor para ler temperatura e clock da CPU.
    /// Não é distribuído com o app: é instalado pelo winget (ou pelo site oficial).
    /// </summary>
    public static class PawnIo
    {
        public const string WingetId = "namazso.PawnIO";
        public const string Website = "https://pawnio.eu";

        public static bool IsInstalled => Reg.KeyExists(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\PawnIO");

        private static string WingetPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");

        /// <summary>Abre a instalação em uma janela visível (o usuário acompanha); sem winget, abre o site oficial.</summary>
        public static void StartInstall()
        {
            if (File.Exists(WingetPath))
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{WingetPath}\" install --id {WingetId} -e --accept-source-agreements --accept-package-agreements & pause\"")
                {
                    UseShellExecute = true,
                });
                return;
            }
            Process.Start(Website);
        }
    }
}
