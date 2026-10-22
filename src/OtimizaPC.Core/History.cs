using System;
using System.IO;

namespace OtimizaPC.Core
{
    /// <summary>
    /// Histórico permanente das alterações feitas no sistema (otimizações, inicialização),
    /// separado do log técnico. Será exibido na tela de Configurações.
    /// </summary>
    public static class History
    {
        private static readonly object Sync = new object();

        public static string FilePath => Path.Combine(AppInfo.DataDirectory, "history.log");

        public static void Record(string area, string message)
        {
            try
            {
                lock (Sync)
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{area}\t{message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao gravar histórico: " + ex.Message);
            }
            Log.Info($"[{area}] {message}");
        }
    }
}
