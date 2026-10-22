using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using OtimizaPC.Hardware.Interop;

namespace OtimizaPC.Hardware.Sensors
{
    /// <summary>Instala e detecta o driver exigido para acessar os sensores da CPU.</summary>
    public static class PawnIo
    {
        public const string WingetId = "namazso.PawnIO";
        public const string Website = "https://pawnio.eu";

        public static bool IsInstalled => Reg.KeyExists(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\PawnIO");

        private static string WingetPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");

        /// <summary>Aguarda o winget e verifica o driver antes de informar sucesso.</summary>
        public static async Task InstallAsync()
        {
            if (IsInstalled) return;
            if (!File.Exists(WingetPath))
                throw new InvalidOperationException("O winget não está disponível. Instale o PawnIO pelo site oficial e use Verificar sensores.");

            using (var process = Process.Start(new ProcessStartInfo(WingetPath,
                $"install --id {WingetId} --exact --source winget --silent --disable-interactivity --accept-source-agreements --accept-package-agreements")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            }))
            {
                if (process == null) throw new InvalidOperationException("Não foi possível iniciar o instalador do PawnIO.");
                await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                if (!IsInstalled)
                    throw new InvalidOperationException($"O PawnIO não foi detectado após a instalação (código {process.ExitCode}). Tente novamente ou use o site oficial.");
            }
        }
    }
}
