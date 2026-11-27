using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace OtimizaPC.Tweaks.Uninstall
{
    internal sealed class LeftoverFileSystem : ILeftoverFileSystem
    {
        public FileAttributes? GetAttributes(string path) => Read(() => (FileAttributes?)File.GetAttributes(path), null);
        public IEnumerable<string> GetDirectories(string path) => Read(() => Directory.GetDirectories(path), Array.Empty<string>());
        public IEnumerable<string> GetFiles(string path) => Read(() => Directory.GetFiles(path, "*.lnk"), Array.Empty<string>());

        private static T Read<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                return fallback;
            }
        }
    }

    internal sealed class LeftoverRegistry : ILeftoverRegistry
    {
        public bool KeyExists(RegistryHive hive, RegistryView view, string path)
        {
            using (var root = RegistryKey.OpenBaseKey(hive, view))
            using (var key = root.OpenSubKey(path, writable: false))
                return key != null;
        }

        public IEnumerable<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string path) =>
            Read(hive, view, path, key => key?.GetSubKeyNames() ?? Array.Empty<string>(), Array.Empty<string>());

        private static T Read<T>(RegistryHive hive, RegistryView view, string path, Func<RegistryKey, T> read, T fallback)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, view))
                using (var key = root.OpenSubKey(path, writable: false))
                    return read(key);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                return fallback;
            }
        }
    }
}
