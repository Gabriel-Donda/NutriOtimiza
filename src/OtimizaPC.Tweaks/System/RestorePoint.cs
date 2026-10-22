using System;
using System.Linq;
using System.Management;
using OtimizaPC.Core;

namespace OtimizaPC.Tweaks.SystemTools
{
    public sealed class RestorePointResult
    {
        public bool Success { get; set; }

        public string Message { get; set; }
    }

    /// <summary>Cria um ponto de restauração do sistema antes de aplicar otimizações.</summary>
    public static class RestorePoint
    {
        private const string Scope = @"\\.\root\default";

        public static RestorePointResult TryCreate(string description)
        {
            if (OsInfo.IsServer)
                return new RestorePointResult { Message = "O Windows Server não tem Restauração do Sistema." };
            if (!AppInfo.IsAdministrator)
                return new RestorePointResult { Message = "Criar ponto de restauração requer administrador." };

            try
            {
                // O Windows cria no máximo um ponto a cada 24 h; nesse caso, o ponto existente é o de segurança
                var recent = LatestRestorePoint();
                if (recent.HasValue && DateTime.Now - recent.Value < TimeSpan.FromHours(24))
                {
                    return new RestorePointResult
                    {
                        Success = true,
                        Message = $"Já existe um ponto de restauração recente ({recent.Value:dd/MM HH:mm}); o Windows não cria outro em menos de 24 h.",
                    };
                }

                using (var restore = new ManagementClass(Scope, "SystemRestore", null))
                using (var input = restore.GetMethodParameters("CreateRestorePoint"))
                {
                    input["Description"] = description;
                    input["RestorePointType"] = 12; // MODIFY_SETTINGS
                    input["EventType"] = 100; // BEGIN_SYSTEM_CHANGE
                    using (var output = restore.InvokeMethod("CreateRestorePoint", input, null))
                    {
                        var code = Convert.ToUInt32(output?["ReturnValue"] ?? 1u);
                        if (code == 0)
                        {
                            History.Record("Restauração", "Ponto de restauração criado: " + description);
                            return new RestorePointResult { Success = true, Message = "Ponto de restauração criado." };
                        }

                        Log.Warn("CreateRestorePoint retornou " + code);
                        return new RestorePointResult
                        {
                            Message = code == 1058
                                ? "A Restauração do Sistema está desativada neste computador."
                                : $"O Windows não criou o ponto de restauração (código {code}). Verifique se a Proteção do Sistema está ativa na unidade C:.",
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao criar ponto de restauração", ex);
                return new RestorePointResult { Message = "Não foi possível criar o ponto de restauração: " + ex.Message };
            }
        }

        private static DateTime? LatestRestorePoint()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(Scope, "SELECT CreationTime FROM SystemRestore"))
                using (var results = searcher.Get())
                {
                    return results.Cast<ManagementBaseObject>()
                        .Select(r => r["CreationTime"] as string)
                        .Where(t => t != null)
                        .Select(t => (DateTime?)ManagementDateTimeConverter.ToDateTime(t))
                        .OrderByDescending(t => t)
                        .FirstOrDefault();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
