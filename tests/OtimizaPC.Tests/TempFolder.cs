using System;
using System.Diagnostics;
using System.IO;

namespace OtimizaPC.Tests
{
    /// <summary>Pasta temporária isolada para cada teste, removida ao final.</summary>
    internal sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OtimizaPC.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string relativePath, int sizeBytes = 100, TimeSpan? age = null)
        {
            var full = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllBytes(full, new byte[sizeBytes]);
            if (age.HasValue) System.IO.File.SetLastWriteTimeUtc(full, DateTime.UtcNow - age.Value);
            return full;
        }

        public string Folder(string relativePath)
        {
            var full = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(full);
            return full;
        }

        /// <summary>Cria uma junction (não exige administrador).</summary>
        public void Junction(string linkRelativePath, string target)
        {
            var link = System.IO.Path.Combine(Path, linkRelativePath);
            using (var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            }))
            {
                process.WaitForExit();
            }
        }

        public void Dispose()
        {
            try
            {
                // Remove junctions antes, para não apagar o conteúdo do alvo
                foreach (var dir in Directory.GetDirectories(Path, "*", SearchOption.AllDirectories))
                {
                    if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0)
                        Directory.Delete(dir, false);
                }
                Directory.Delete(Path, true);
            }
            catch
            {
                // Limpeza de teste é best-effort
            }
        }
    }
}
