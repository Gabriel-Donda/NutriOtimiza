using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using OtimizaPC.Core;
using OtimizaPC.Tweaks.Registry;

namespace OtimizaPC.Tweaks.Startup
{
    public enum StartupSource
    {
        UserRun,
        MachineRun,
        MachineRun32,
        UserFolder,
        CommonFolder,
    }

    public enum StartupApprovalState { NotConfigured, Enabled, Disabled, InvalidData, AccessDenied, ReadError }

    public sealed class StartupEntry
    {
        public string Name { get; set; }

        public string Command { get; set; }

        public string ExecutablePath { get; set; }

        public string Publisher { get; set; }

        public StartupSource Source { get; set; }

        /// <summary>Projeção para consumidores antigos; consulte IsStateKnown antes de exibir o estado.</summary>
        public bool IsEnabled { get; set; }
        public StartupApprovalState ApprovalState { get; internal set; }
        public string ApprovalMessage { get; internal set; }
        public bool IsStateKnown => ApprovalState == StartupApprovalState.NotConfigured ||
            ApprovalState == StartupApprovalState.Enabled || ApprovalState == StartupApprovalState.Disabled;

        public bool RequiresAdmin => Source == StartupSource.MachineRun || Source == StartupSource.MachineRun32 || Source == StartupSource.CommonFolder;

        public string LocationText
        {
            get
            {
                switch (Source)
                {
                    case StartupSource.UserRun: return "Registro (usuário)";
                    case StartupSource.MachineRun: return "Registro (todos os usuários)";
                    case StartupSource.MachineRun32: return "Registro 32 bits (todos os usuários)";
                    case StartupSource.UserFolder: return "Pasta Inicializar (usuário)";
                    default: return "Pasta Inicializar (todos os usuários)";
                }
            }
        }
    }

    /// <summary>
    /// Lista e ativa/desativa programas da inicialização usando o mesmo mecanismo do Gerenciador de Tarefas
    /// (chaves StartupApproved). Nada é apagado: desativar é sempre reversível.
    /// </summary>
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

        public static IReadOnlyList<StartupEntry> List()
        {
            var entries = new List<StartupEntry>();
            AddRegistry(entries, RegistryHive.CurrentUser, RunKey, StartupSource.UserRun);
            AddRegistry(entries, RegistryHive.LocalMachine, RunKey, StartupSource.MachineRun);
            if (Environment.Is64BitOperatingSystem)
                AddRegistry(entries, RegistryHive.LocalMachine, Run32Key, StartupSource.MachineRun32);
            AddFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.UserFolder);
            AddFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.CommonFolder);

            foreach (var entry in entries)
                ReadApproval(entry);
            return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static void SetEnabled(StartupEntry entry, bool enabled)
        {
            var (hive, path) = ApprovedLocation(entry.Source);
            using (var root = RegistryAccess.BaseKey(hive))
            using (var key = root.CreateSubKey(path, writable: true))
            {
                key.SetValue(entry.Name, ApprovedValue(enabled), RegistryValueKind.Binary);
            }
            entry.IsEnabled = enabled;
            entry.ApprovalState = enabled ? StartupApprovalState.Enabled : StartupApprovalState.Disabled;
            entry.ApprovalMessage = null;
            History.Record("Inicialização", $"{(enabled ? "Ativado" : "Desativado")}: {entry.Name} ({entry.LocationText})");
        }

        /// <summary>Formato do Gerenciador de Tarefas: 02 = ativado; 03 + data (FILETIME) = desativado.</summary>
        internal static byte[] ApprovedValue(bool enabled)
        {
            var value = new byte[12];
            value[0] = enabled ? (byte)2 : (byte)3;
            if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(value, 4);
            return value;
        }

        internal static StartupApprovalState ParseApproval(object value)
        {
            if (value == null) return StartupApprovalState.NotConfigured;
            if (!(value is byte[] data) || data.Length != 12 || data[1] != 0 || data[2] != 0 || data[3] != 0)
                return StartupApprovalState.InvalidData;
            switch (data[0])
            {
                case 2: case 6: return StartupApprovalState.Enabled;
                case 3: case 7: return StartupApprovalState.Disabled;
                default: return StartupApprovalState.InvalidData;
            }
        }

        internal static void ReadApproval(StartupEntry entry, Func<RegistryHive, string, string, object> read = null)
        {
            var (hive, path) = ApprovedLocation(entry.Source);
            try
            {
                object value;
                if (read != null) value = read(hive, path, entry.Name);
                else using (var key = RegistryAccess.Open(hive, path, writable: false)) value = key?.GetValue(entry.Name);
                entry.ApprovalState = ParseApproval(value);
                entry.ApprovalMessage = entry.ApprovalState == StartupApprovalState.InvalidData
                    ? "Dados StartupApproved inválidos; estado desconhecido." : null;
            }
            catch (Exception ex)
            {
                entry.ApprovalState = ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                    ? StartupApprovalState.AccessDenied : StartupApprovalState.ReadError;
                entry.ApprovalMessage = (entry.ApprovalState == StartupApprovalState.AccessDenied
                    ? "Acesso negado ao StartupApproved: " : "Falha ao ler StartupApproved: ") + ex.Message;
            }
            entry.IsEnabled = entry.ApprovalState == StartupApprovalState.Enabled || entry.ApprovalState == StartupApprovalState.NotConfigured;
            if (entry.ApprovalMessage != null) Log.Warn($"{entry.Name} ({entry.LocationText}): {entry.ApprovalMessage}");
        }

        private static (RegistryHive Hive, string Path) ApprovedLocation(StartupSource source)
        {
            switch (source)
            {
                case StartupSource.UserRun: return (RegistryHive.CurrentUser, ApprovedRoot + @"\Run");
                case StartupSource.MachineRun: return (RegistryHive.LocalMachine, ApprovedRoot + @"\Run");
                case StartupSource.MachineRun32: return (RegistryHive.LocalMachine, ApprovedRoot + @"\Run32");
                case StartupSource.UserFolder: return (RegistryHive.CurrentUser, ApprovedRoot + @"\StartupFolder");
                default: return (RegistryHive.LocalMachine, ApprovedRoot + @"\StartupFolder");
            }
        }

        private static void AddRegistry(List<StartupEntry> entries, RegistryHive hive, string path, StartupSource source)
        {
            try
            {
                using (var key = RegistryAccess.Open(hive, path, writable: false))
                {
                    if (key == null) return;
                    foreach (var name in key.GetValueNames().Where(n => !string.IsNullOrEmpty(n)))
                    {
                        var command = key.GetValue(name)?.ToString();
                        if (string.IsNullOrWhiteSpace(command)) continue;
                        var exe = ExtractExecutable(command);
                        entries.Add(new StartupEntry
                        {
                            Name = name,
                            Command = command,
                            ExecutablePath = exe,
                            Publisher = Publisher(exe),
                            Source = source,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Falha ao ler {hive}\\{path}: {ex.Message}");
            }
        }

        private static void AddFolder(List<StartupEntry> entries, string folder, StartupSource source)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (var file in Directory.GetFiles(folder).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                {
                    entries.Add(new StartupEntry
                    {
                        // Em StartupApproved\StartupFolder o nome do valor é o nome do arquivo (com extensão)
                        Name = Path.GetFileName(file),
                        Command = file,
                        ExecutablePath = file,
                        Publisher = Publisher(file),
                        Source = source,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Falha ao ler a pasta {folder}: {ex.Message}");
            }
        }

        /// <summary>Extrai o caminho do executável de uma linha de comando (com ou sem aspas).</summary>
        internal static string ExtractExecutable(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            var text = Environment.ExpandEnvironmentVariables(command.Trim());

            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = text.IndexOf('"', 1);
                return end > 1 ? text.Substring(1, end - 1) : text.Trim('"');
            }

            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0) return text.Substring(0, exe + 4);

            var space = text.IndexOf(' ');
            return space > 0 ? text.Substring(0, space) : text;
        }

        private static string Publisher(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
                var company = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim();
                return string.IsNullOrEmpty(company) ? null : company;
            }
            catch
            {
                return null;
            }
        }
    }
}
