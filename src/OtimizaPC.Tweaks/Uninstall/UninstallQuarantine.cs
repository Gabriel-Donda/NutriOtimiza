using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using OtimizaPC.Core;
using static OtimizaPC.Tweaks.Uninstall.UninstallQuarantineFiles;

namespace OtimizaPC.Tweaks.Uninstall
{
    public sealed class UninstallQuarantineOperation
    {
        internal UninstallQuarantineOperation(string id, string programName, DateTime createdUtc)
        {
            Id = id;
            ProgramName = programName;
            CreatedUtc = createdUtc;
        }

        internal string Id { get; }
        public string ProgramName { get; }
        public DateTime CreatedUtc { get; }
        public string DateText => CreatedUtc.ToLocalTime().ToString("g");
    }

    public sealed class UninstallQuarantineResult
    {
        public const string StillInstalledMessage = "O programa parece ainda estar instalado";

        public int Completed { get; internal set; }
        public bool Blocked { get; internal set; }
        public IReadOnlyList<string> Skipped => _skipped;

        private readonly List<string> _skipped = new List<string>();

        internal void Skip(string path, string reason)
        {
            var message = path + " — " + reason;
            _skipped.Add(message);
            Log.Warn("Quarentena do desinstalador: " + message);
        }
    }

    public sealed class UninstallQuarantine
    {
        private const string TimestampFormat = "yyyyMMdd-HHmmss";
        private const string ManifestName = "manifest.json";
        private static readonly Regex OperationName = new Regex(
            @"\A(?<date>[0-9]{8}-[0-9]{6})_(?<name>[\p{L}\p{Nd} _.-]{1,60})_[0-9a-f]{32}\z",
            RegexOptions.CultureInvariant);
        private static readonly SecurityIdentifier Administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier SystemAccount = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private readonly LeftoverScanner _scanner;
        private readonly string _root;

        public UninstallQuarantine(LeftoverScanner scanner)
        {
            _scanner = scanner;
            _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                AppInfo.Name, "UninstallQuarantine");
        }

        public UninstallQuarantineResult Remove(InstalledProgram program, IReadOnlyList<UninstallLeftover> selected)
        {
            var result = new UninstallQuarantineResult();
            if (_scanner.UninstallKeyExists(program))
            {
                result.Blocked = true;
                foreach (var item in selected)
                    result.Skip(item.Path, UninstallQuarantineResult.StillInstalledMessage);
                return result;
            }

            if (selected.Count == 0) return result;
            if (!CheckRoot(create: true)) throw new IOException("A pasta da quarentena não é segura.");

            var now = DateTime.UtcNow;
            now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
            var id = now.ToString(TimestampFormat, CultureInfo.InvariantCulture) + "_" +
                SanitizeName(program.DisplayName) + "_" + Guid.NewGuid().ToString("N");
            var operationPath = Path.Combine(_root, id);
            Directory.CreateDirectory(operationPath);
            Directory.CreateDirectory(Path.Combine(operationPath, "files"));
            Directory.CreateDirectory(Path.Combine(operationPath, "registry"));

            // A ordem inversa na restauração permite devolver primeiro as pastas que contêm outros itens.
            var manifest = new UninstallQuarantineManifest
            {
                ProgramName = program.DisplayName,
                UserSid = CurrentUserSid(),
                CreatedUtc = now,
                Items = selected.OrderByDescending(item => item.Path.Length).Select((item, index) => new QuarantinedLeftover
                {
                    Kind = item.Kind,
                    OriginalPath = item.Path,
                    BackupName = index.ToString("D6", CultureInfo.InvariantCulture) +
                        (item.Kind == LeftoverKind.RegistryKey ? ".reg" : item.Kind == LeftoverKind.Shortcut ? ".lnk" : ""),
                    Hive = item.Hive,
                    View = item.View,
                    RegistryPath = item.RegistryPath
                }).ToList()
            };

            // O manifesto completo existe antes da primeira alteração. A cópia presente indica o que pode ser devolvido.
            using (var stream = new FileStream(Path.Combine(operationPath, ManifestName), FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                manifest.Write(stream);
                stream.Flush(flushToDisk: true);
            }

            foreach (var item in manifest.Items)
            {
                if (result.Blocked || _scanner.UninstallKeyExists(program))
                {
                    result.Blocked = true;
                    result.Skip(item.OriginalPath, UninstallQuarantineResult.StillInstalledMessage);
                    continue;
                }

                ProcessItem(result, item, () =>
                {
                    var backup = BackupPath(operationPath, item);
                    if (item.Kind == LeftoverKind.RegistryKey)
                        UninstallQuarantineRegistry.Remove(item, backup);
                    else
                        Move(_scanner, item.Kind, item.OriginalPath, backup, _root, restore: false, MoveOnSameVolume);
                });
            }

            RemoveIfEmpty(operationPath);
            Log.Info($"Quarentena do desinstalador ({id}): {result.Completed} removidos, {result.Skipped.Count} pulados.");
            return result;
        }

        public IReadOnlyList<UninstallQuarantineOperation> List()
        {
            var operations = new List<UninstallQuarantineOperation>();
            if (!CheckRoot(create: false)) return operations;

            foreach (var path in Directory.GetDirectories(_root))
            {
                var id = Path.GetFileName(path);
                if (!TryReadId(id, out var created, out var name) || !IsSafeDirectory(path)) continue;

                try
                {
                    name = ReadManifest(path).ProgramName ?? name;
                }
                catch (Exception ex) when (IsOperationError(ex))
                {
                    Log.Error("Falha ao ler manifesto da quarentena do desinstalador: " + id, ex);
                }

                operations.Add(new UninstallQuarantineOperation(id, name, created));
            }

            return operations.OrderByDescending(operation => operation.CreatedUtc).ToList();
        }

        public UninstallQuarantineResult Restore(UninstallQuarantineOperation operation)
        {
            var operationPath = GetOperationPath(operation);
            var manifest = ReadManifest(operationPath);
            if (!string.Equals(manifest.UserSid, CurrentUserSid(), StringComparison.Ordinal))
                throw new IOException("A operação pertence a outra conta do Windows.");

            var result = new UninstallQuarantineResult();
            foreach (var item in manifest.Items.AsEnumerable().Reverse())
            {
                try
                {
                    var backup = BackupPath(operationPath, item);
                    if (!Exists(backup)) continue;

                    if (item.Kind == LeftoverKind.RegistryKey)
                        UninstallQuarantineRegistry.Restore(item, backup);
                    else
                        Move(_scanner, item.Kind, item.OriginalPath, backup, _root, restore: true, MoveOnSameVolume);

                    result.Completed++;
                }
                catch (Exception ex) when (IsOperationError(ex))
                {
                    result.Skip(item.OriginalPath, ex.Message);
                }
            }

            RemoveIfEmpty(operationPath);
            Log.Info($"Quarentena do desinstalador ({operation.Id}): {result.Completed} restaurados, {result.Skipped.Count} pulados.");
            return result;
        }

        public void Delete(UninstallQuarantineOperation operation)
        {
            DeleteTree(_root, GetOperationPath(operation));
            Log.Info("Quarentena do desinstalador excluída definitivamente: " + operation.Id);
        }

        public void PurgeExpired()
        {
            if (!CheckRoot(create: false)) return;

            var now = DateTime.UtcNow;
            foreach (var path in Directory.GetDirectories(_root))
            {
                var id = Path.GetFileName(path);
                if (!TryReadId(id, out var created, out _) || !IsExpired(created, now)) continue;

                try
                {
                    Delete(new UninstallQuarantineOperation(id, null, created));
                }
                catch (Exception ex) when (IsOperationError(ex))
                {
                    Log.Error("Falha ao excluir quarentena expirada: " + id, ex);
                }
            }
        }

        internal static bool IsExpired(DateTime createdUtc, DateTime nowUtc) =>
            nowUtc - createdUtc > TimeSpan.FromDays(7);

        internal static string SanitizeName(string name)
        {
            var sanitized = Regex.Replace(name ?? "", @"[^\p{L}\p{Nd} _.-]", "_");
            if (sanitized.Length > 60) sanitized = sanitized.Substring(0, 60);
            sanitized = sanitized.Trim(' ', '.');
            return sanitized.Length == 0 ? "programa" : sanitized;
        }

        private static bool TryReadId(string id, out DateTime created, out string name)
        {
            var match = OperationName.Match(id);
            name = match.Groups["name"].Value;
            return DateTime.TryParseExact(match.Groups["date"].Value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out created);
        }

        private string GetOperationPath(UninstallQuarantineOperation operation)
        {
            if (operation == null || !TryReadId(operation.Id, out _, out _) || !CheckRoot(create: false))
                throw new IOException("Operação da quarentena inválida ou indisponível.");

            var path = Path.Combine(_root, operation.Id);
            if (!IsBelow(path, _root) || !IsSafeDirectory(path))
                throw new IOException("O caminho da operação não é seguro.");

            return path;
        }

        private static string BackupPath(string operationPath, QuarantinedLeftover item)
        {
            if (!Regex.IsMatch(item.BackupName ?? "", @"\A[0-9]{6,}(?:\.reg|\.lnk)?\z"))
                throw new IOException("Nome de backup inválido.");

            var folder = Path.Combine(operationPath, item.Kind == LeftoverKind.RegistryKey ? "registry" : "files");
            if (!IsSafeDirectory(folder)) throw new IOException("A pasta de backup não é segura.");

            return Path.Combine(folder, item.BackupName);
        }

        private static UninstallQuarantineManifest ReadManifest(string path)
        {
            var file = Path.Combine(path, ManifestName);
            if (!TryGetSafeAttributes(file, out var attributes) || (attributes & FileAttributes.Directory) != 0)
                throw new IOException("Manifesto ausente ou inválido.");

            using (var stream = File.OpenRead(file))
            {
                var manifest = UninstallQuarantineManifest.Read(stream);
                if (manifest?.Items == null || manifest.Items.Any(item => item == null))
                    throw new SerializationException("Manifesto da quarentena inválido.");
                return manifest;
            }
        }

        private void RemoveIfEmpty(string operationPath)
        {
            try
            {
                var files = Path.Combine(operationPath, "files");
                var registry = Path.Combine(operationPath, "registry");
                if (IsSafeDirectory(files) && IsSafeDirectory(registry) &&
                    !Directory.EnumerateFileSystemEntries(files).Any() &&
                    !Directory.EnumerateFileSystemEntries(registry).Any())
                    DeleteTree(_root, operationPath);
            }
            catch (Exception ex) when (IsOperationError(ex))
            {
                Log.Error("Falha ao excluir operação vazia da quarentena: " + operationPath, ex);
            }
        }

        private static void ProcessItem(UninstallQuarantineResult result, QuarantinedLeftover item, Action action)
        {
            try
            {
                action();
                result.Completed++;
            }
            catch (Exception ex) when (IsOperationError(ex))
            {
                result.Skip(item.OriginalPath, ex.Message);
            }
        }

        private bool CheckRoot(bool create)
        {
            var appDirectory = Path.GetDirectoryName(_root);
            if (!IsSafeDirectory(Path.GetDirectoryName(appDirectory)))
                throw new IOException("O caminho de ProgramData não é seguro.");

            return CheckPrivateDirectory(appDirectory, create) && CheckPrivateDirectory(_root, create);
        }

        private static bool CheckPrivateDirectory(string path, bool create)
        {
            if (!Exists(path))
            {
                if (!create) return false;

                // Os arquivos .reg são importados com privilégios elevados. Só administradores e SYSTEM podem gravá-los.
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.SetOwner(Administrators);
                foreach (var sid in new[] { Administrators, SystemAccount })
                    security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                        AccessControlType.Allow));
                Directory.CreateDirectory(path, security);
            }

            if (!IsSafeDirectory(path)) throw new IOException("O caminho da quarentena não é seguro.");
            var acl = Directory.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access);
            if (!IsTrustedSid(acl.GetOwner(typeof(SecurityIdentifier))) ||
                acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>()
                    .Any(rule => rule.AccessControlType == AccessControlType.Allow && !IsTrustedSid(rule.IdentityReference)))
                throw new SecurityException("A pasta da quarentena tem permissões incompatíveis.");
            return true;
        }

        private static bool IsTrustedSid(IdentityReference sid) => Administrators.Equals(sid) || SystemAccount.Equals(sid);

        private static string CurrentUserSid()
        {
            using (var user = WindowsIdentity.GetCurrent())
                return user.User.Value;
        }
    }
}
