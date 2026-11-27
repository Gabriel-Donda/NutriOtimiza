using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using static OtimizaPC.Tweaks.Uninstall.UninstallQuarantineFiles;

namespace OtimizaPC.Tweaks.Uninstall
{
    internal static class UninstallQuarantineRegistry
    {
        internal static void Remove(QuarantinedLeftover item, string backup)
        {
            var key = GetKeyName(item);
            var pendingBackup = backup + ".pending.reg";
            try
            {
                RunReg(item.View, $"export \"{key}\" \"{pendingBackup}\" /y");
                if (!TryGetSafeAttributes(pendingBackup, out var attributes) || (attributes & FileAttributes.Directory) != 0)
                    throw new IOException("O arquivo de backup do Registro não foi criado.");

                ValidateExport(pendingBackup, key);

                // Publica o backup somente depois que o reg.exe confirma a exportação.
                File.Move(pendingBackup, backup);
                if (!File.Exists(backup)) throw new IOException("O arquivo de backup do Registro desapareceu.");
                using (var root = RegistryKey.OpenBaseKey(item.Hive, item.View))
                    root.DeleteSubKeyTree(item.RegistryPath, throwOnMissingSubKey: true);
            }
            finally
            {
                if (File.Exists(pendingBackup) && IsSafeDirectory(Path.GetDirectoryName(pendingBackup)))
                    File.Delete(pendingBackup);
            }
        }

        internal static void Restore(QuarantinedLeftover item, string backup)
        {
            var keyName = GetKeyName(item);
            if (!TryGetSafeAttributes(backup, out var attributes) || (attributes & FileAttributes.Directory) != 0)
                throw new IOException("O backup do Registro não é seguro.");

            ValidateExport(backup, keyName);
            using (var root = RegistryKey.OpenBaseKey(item.Hive, item.View))
            using (var key = root.OpenSubKey(item.RegistryPath))
                if (key != null) throw new IOException("Conflito: a chave já existe.");

            RunReg(item.View, $"import \"{backup}\"");
            File.Delete(backup);
        }

        private static void ValidateExport(string backup, string key)
        {
            var expected = key.StartsWith(@"HKCU\", StringComparison.Ordinal)
                ? "HKEY_CURRENT_USER" + key.Substring(4)
                : "HKEY_LOCAL_MACHINE" + key.Substring(4);
            var foundRoot = false;

            // Nomes de subchaves podem vir de software de terceiros. O arquivo importado só pode atuar na árvore exportada.
            using (var reader = new StreamReader(backup))
            {
                if (reader.ReadLine() != "Windows Registry Editor Version 5.00")
                    throw new IOException("Cabeçalho do arquivo .reg inválido.");

                for (var line = reader.ReadLine(); line != null; line = reader.ReadLine())
                {
                    if (line.IndexOf('\0') >= 0)
                        throw new IOException("Caractere inválido no arquivo .reg.");

                    line = line.Trim();
                    if (!line.StartsWith("[", StringComparison.Ordinal)) continue;
                    if (!line.EndsWith("]", StringComparison.Ordinal) || line.IndexOf(']') != line.Length - 1)
                        throw new IOException("Seção inválida no arquivo .reg.");

                    var path = line.Substring(1, line.Length - 2);
                    if (string.Equals(path, expected, StringComparison.OrdinalIgnoreCase))
                        foundRoot = true;
                    else if (!path.StartsWith(expected + "\\", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("O arquivo .reg contém uma chave fora da árvore exportada.");
                }
            }

            if (!foundRoot) throw new IOException("A chave exportada não consta no arquivo .reg.");
        }

        private static string GetKeyName(QuarantinedLeftover item)
        {
            if ((item.Hive != RegistryHive.LocalMachine && item.Hive != RegistryHive.CurrentUser) ||
                (item.View != RegistryView.Registry32 && item.View != RegistryView.Registry64) ||
                string.IsNullOrEmpty(item.RegistryPath) ||
                !item.RegistryPath.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase) ||
                item.RegistryPath.EndsWith(@"\", StringComparison.Ordinal) ||
                item.RegistryPath.IndexOfAny(new[] { '"', '\0', '\r', '\n' }) >= 0)
                throw new IOException("Caminho do Registro inválido ou incompatível com o reg.exe.");

            return (item.Hive == RegistryHive.CurrentUser ? "HKCU\\" : "HKLM\\") + item.RegistryPath;
        }

        private static void RunReg(RegistryView view, string arguments)
        {
            var systemFolder = "System32";
            if (Environment.Is64BitOperatingSystem)
            {
                if (view == RegistryView.Registry32) systemFolder = "SysWOW64";
                else if (!Environment.Is64BitProcess) systemFolder = "Sysnative";
            }

            // O executável da arquitetura correspondente seleciona a mesma visão para exportar e importar.
            var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), systemFolder, "reg.exe");
            using (var process = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                if (process == null) throw new IOException("Não foi possível iniciar o reg.exe.");
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new IOException($"O reg.exe retornou o código {process.ExitCode}.");
            }
        }
    }
}
