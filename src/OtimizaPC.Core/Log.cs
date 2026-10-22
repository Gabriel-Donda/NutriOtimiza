using System;
using System.IO;

namespace OtimizaPC.Core
{
    /// <summary>Log simples em arquivo diário (%LocalAppData%\OtimizaPC\logs).</summary>
    public static class Log
    {
        private static readonly object Sync = new object();

        public static void Info(string message) => Write("INFO", message);

        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message, Exception ex = null) =>
            Write("ERRO", ex == null ? message : message + Environment.NewLine + ex);

        private static void Write(string level, string message)
        {
            try
            {
                var dir = Path.Combine(AppInfo.DataDirectory, "logs");
                var file = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                lock (Sync)
                {
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Falha de log nunca deve derrubar o app
            }
        }
    }
}
