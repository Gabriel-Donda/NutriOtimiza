using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace OtimizaPC.Tweaks.Uninstall
{
    internal static class UninstallQuarantineFiles
    {
        internal static bool IsBelow(string path, string root) =>
            path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

        internal static bool IsSafeDirectory(string path) =>
            TryGetSafeAttributes(path, out var attributes) && (attributes & FileAttributes.Directory) != 0;

        internal static bool TryGetSafeAttributes(string path, out FileAttributes attributes)
        {
            attributes = default(FileAttributes);
            var parent = Path.GetDirectoryName(path);
            if (parent != null && !IsSafeDirectory(parent)) return false;

            try
            {
                attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.ReparsePoint) == 0;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        internal static bool Exists(string path)
        {
            try
            {
                File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        internal static void Move(LeftoverScanner scanner, LeftoverKind kind, string originalPath, string backupPath,
            string quarantineRoot, bool restore, Action<string, string> move)
        {
            if (!IsBelow(backupPath, quarantineRoot) || !IsSafeDirectory(Path.GetDirectoryName(backupPath)) ||
                !scanner.IsAllowedFilePath(originalPath, kind) || IsBelow(originalPath, quarantineRoot) ||
                IsBelow(quarantineRoot, originalPath) ||
                string.Equals(originalPath, quarantineRoot, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Caminho fora das raízes permitidas ou com ponto de reanálise.");

            if (restore)
            {
                if (!IsSafeDirectory(Path.GetDirectoryName(originalPath)))
                    throw new IOException("A pasta de destino não existe ou não é segura.");
                if (Exists(originalPath))
                    throw new IOException("Conflito: o destino já existe.");
                if (!TryGetSafeAttributes(backupPath, out var attributes) ||
                    ((attributes & FileAttributes.Directory) != 0) != (kind == LeftoverKind.Folder))
                    throw new IOException("A cópia em quarentena mudou ou não é segura.");

                move(backupPath, originalPath);
                return;
            }

            if (!scanner.IsSafeFile(originalPath, kind))
                throw new IOException("O item não existe mais, mudou de tipo ou tem um ponto de reanálise no caminho.");

            move(originalPath, backupPath);
        }

        internal static void MoveOnSameVolume(string source, string destination)
        {
            const int writeThrough = 0x8;

            if ((File.GetAttributes(source) & FileAttributes.Directory) == 0)
            {
                // Também ignora arquivos abertos com compartilhamento que permitiria a renomeação.
                using (File.Open(source, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            }

            // Sem substituição, cópia entre volumes ou adiamento para a reinicialização.
            if (MoveFileEx(source, destination, writeThrough)) return;

            var error = Marshal.GetLastWin32Error();
            if (error == 17) throw new IOException("O destino fica em outro volume.");
            if (error == 32 || error == 33) throw new IOException("O item está em uso.");
            if (error == 80 || error == 183) throw new IOException("Conflito: o destino já existe.");
            throw new Win32Exception(error);
        }

        internal static void DeleteTree(string root, string path, bool operationFolder = true)
        {
            if (!IsBelow(path, root) || !string.Equals(path, Path.GetFullPath(path), StringComparison.Ordinal) ||
                !IsSafeDirectory(Path.GetDirectoryName(path)))
                throw new IOException("O caminho de exclusão não é seguro.");

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                File.Delete(path);
                return;
            }

            if ((attributes & FileAttributes.ReparsePoint) == 0 && IsSafeDirectory(path))
            {
                var manifest = operationFolder ? Path.Combine(path, "manifest.json") : null;
                foreach (var entry in Directory.GetFileSystemEntries(path))
                    if (!string.Equals(entry, manifest, StringComparison.OrdinalIgnoreCase))
                        DeleteTree(root, entry, operationFolder: false);

                // Preserva o manifesto se a exclusão de algum item falhar.
                if (manifest != null && Exists(manifest))
                    DeleteTree(root, manifest, operationFolder: false);
            }

            if (!IsSafeDirectory(Path.GetDirectoryName(path)))
                throw new IOException("O caminho de exclusão mudou.");
            // Remove uma junção como link, nunca percorre nem apaga o destino.
            Directory.Delete(path, recursive: false);
        }

        internal static bool IsOperationError(Exception ex) =>
            ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException ||
            ex is Win32Exception || ex is ArgumentException || ex is NotSupportedException ||
            ex is System.Runtime.Serialization.SerializationException || ex is System.Xml.XmlException;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);
    }
}
