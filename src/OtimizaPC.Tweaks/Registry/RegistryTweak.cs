using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using OtimizaPC.Core;

namespace OtimizaPC.Tweaks.Registry
{
    /// <summary>Otimização feita por um conjunto de valores no registro.</summary>
    public class RegistryTweak : Tweak
    {
        public RegistryTweak(string id, string name, string description, string category)
            : base(id, name, description, category)
        {
        }

        public List<RegistryChange> Changes { get; } = new List<RegistryChange>();

        public override bool RequiresAdmin => Changes.Any(c => c.Hive != RegistryHive.CurrentUser);

        public override TweakState GetState()
        {
            var current = Changes.Count(c =>
            {
                try
                {
                    return c.IsCurrent();
                }
                catch
                {
                    return false;
                }
            });
            if (current == Changes.Count) return TweakState.Applied;
            return current == 0 ? TweakState.NotApplied : TweakState.Partial;
        }

        public override void Prepare(UndoRecord record)
        {
            foreach (var change in Changes)
            {
                var hive = change.Hive.ToString();
                // Reaplicar não sobrescreve o original já guardado
                if (record.HasRegistryBackup(hive, change.Path, change.Name)) continue;

                var backup = new RegistryBackup
                {
                    Hive = hive,
                    Path = change.Path,
                    Name = change.Name,
                    CreatedKey = RegistryAccess.FirstMissingKey(change.Hive, change.Path),
                };

                using (var key = RegistryAccess.Open(change.Hive, change.Path, writable: false))
                {
                    var existing = key?.GetValue(change.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (existing != null)
                    {
                        var kind = key.GetValueKind(change.Name);
                        backup.Existed = true;
                        backup.Kind = kind.ToString();
                        backup.Data = RegistryChange.Serialize(existing, kind);
                    }
                }
                record.Registry.Add(backup);
            }
        }

        public override void Apply(UndoRecord record)
        {
            foreach (var change in Changes)
            {
                using (var root = RegistryAccess.BaseKey(change.Hive))
                using (var key = root.CreateSubKey(change.Path, writable: true))
                {
                    key.SetValue(change.Name, change.Value, change.Kind);
                }
            }
            AfterApply();
        }

        public override void Revert(UndoRecord record)
        {
            if (record != null && record.Registry.Count > 0)
            {
                // Ordem inversa: desfaz primeiro o que foi alterado por último
                foreach (var backup in Enumerable.Reverse(record.Registry))
                    Restore(backup);
            }
            else
            {
                foreach (var change in Changes)
                    RestoreDefault(change);
            }
            AfterRevert();
        }

        /// <summary>Ação extra após aplicar (ex.: parar um serviço).</summary>
        protected virtual void AfterApply()
        {
        }

        protected virtual void AfterRevert()
        {
        }

        private static void Restore(RegistryBackup backup)
        {
            var hive = (RegistryHive)Enum.Parse(typeof(RegistryHive), backup.Hive);
            using (var root = RegistryAccess.BaseKey(hive))
            {
                if (backup.Existed)
                {
                    using (var key = root.OpenSubKey(backup.Path, writable: true))
                    {
                        if (key == null) return;
                        var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), backup.Kind);
                        key.SetValue(backup.Name, RegistryChange.Deserialize(backup.Data, kind), kind);
                    }
                    return;
                }

                // O valor não existia: remove e, se o app criou chaves para ele, apaga as que ficaram vazias
                DeleteValueAndPrune(root, backup.Path, backup.Name, backup.CreatedKey);
            }
        }

        private static void RestoreDefault(RegistryChange change)
        {
            using (var root = RegistryAccess.BaseKey(change.Hive))
            {
                if (change.DefaultValue != null)
                {
                    using (var key = root.OpenSubKey(change.Path, writable: true))
                        key?.SetValue(change.Name, change.DefaultValue, change.Kind);
                }
                else
                {
                    DeleteValueAndPrune(root, change.Path, change.Name, change.DefaultDeleteKey);
                }
            }
            Log.Info($"Revertido para o padrão do Windows (sem registro de desfazer): {change}");
        }

        /// <summary>
        /// Remove o valor e depois as chaves vazias, subindo até <paramref name="topKey"/> (inclusive).
        /// Nunca apaga uma chave que ainda tenha valores ou subchaves de outro programa.
        /// </summary>
        internal static void DeleteValueAndPrune(RegistryKey root, string path, string name, string topKey)
        {
            using (var key = root.OpenSubKey(path, writable: true))
            {
                if (key == null) return;
                key.DeleteValue(name, throwOnMissingValue: false);
            }

            if (string.IsNullOrEmpty(topKey) || !path.StartsWith(topKey, StringComparison.OrdinalIgnoreCase)) return;

            var current = path;
            while (current.Length >= topKey.Length)
            {
                using (var key = root.OpenSubKey(current))
                {
                    if (key == null || key.ValueCount > 0 || key.SubKeyCount > 0) return;
                }
                root.DeleteSubKey(current, throwOnMissingSubKey: false);

                var separator = current.LastIndexOf('\\');
                if (separator < 0) return;
                current = current.Substring(0, separator);
            }
        }
    }
}
