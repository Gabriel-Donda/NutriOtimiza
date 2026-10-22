using System;
using System.Collections.Generic;
using System.Linq;
using OtimizaPC.Hardware.Interop;

namespace OtimizaPC.Repair.Diagnostics
{
    public sealed class DeviceProblem
    {
        public string Name { get; set; }

        public string Class { get; set; }

        public int ErrorCode { get; set; }

        public string Explanation { get; set; }

        /// <summary>Dispositivo desativado pelo usuário: informativo, não é falha.</summary>
        public bool IsDisabled => ErrorCode == 22;

        public string DeviceId { get; set; }
    }

    public sealed class DriverInfo
    {
        public string Device { get; set; }

        public string Class { get; set; }

        public string Provider { get; set; }

        public string Version { get; set; }

        public DateTime? Date { get; set; }

        public bool IsSigned { get; set; }

        public bool IsMicrosoft => Provider != null && Provider.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Dispositivos com erro (ponto de exclamação no Gerenciador de Dispositivos) e lista de drivers.</summary>
    public static class DeviceAnalyzer
    {
        private static readonly Dictionary<int, string> ErrorCodes = new Dictionary<int, string>
        {
            [1] = "Não está configurado corretamente. Reinstale o driver.",
            [3] = "O driver pode estar corrompido ou o sistema está com pouca memória.",
            [10] = "O dispositivo não pode ser iniciado. Atualize o driver ou verifique a conexão física.",
            [12] = "Conflito de recursos com outro dispositivo.",
            [14] = "Requer reiniciar o computador.",
            [18] = "Reinstale os drivers deste dispositivo.",
            [19] = "Informações de configuração no registro estão corrompidas.",
            [21] = "O Windows está removendo o dispositivo.",
            [22] = "Desativado pelo usuário.",
            [24] = "Não está presente, não funciona corretamente ou não tem todos os drivers.",
            [28] = "Driver não instalado. Baixe o driver no site do fabricante do PC ou do componente.",
            [29] = "Desativado no firmware (BIOS/UEFI).",
            [31] = "O Windows não conseguiu carregar o driver.",
            [32] = "O serviço do driver está desativado.",
            [37] = "O driver retornou uma falha ao iniciar.",
            [38] = "Uma versão anterior do driver ainda está na memória: reinicie.",
            [39] = "O driver está corrompido ou ausente.",
            [40] = "Informações do driver no registro estão faltando ou inválidas.",
            [41] = "O driver carregou, mas o hardware não foi encontrado.",
            [43] = "O dispositivo reportou um problema e foi parado pelo Windows (comum em USB e placas de vídeo).",
            [45] = "O dispositivo não está conectado no momento.",
            [48] = "O driver foi bloqueado por ter problemas conhecidos.",
            [52] = "A assinatura digital do driver não pôde ser verificada.",
        };

        public static IReadOnlyList<DeviceProblem> ListProblems() =>
            Wmi.Query("SELECT Name, PNPClass, ConfigManagerErrorCode, DeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0")
                .Select(r =>
                {
                    var code = r.Int("ConfigManagerErrorCode") ?? 0;
                    return new DeviceProblem
                    {
                        Name = r.Str("Name") ?? r.Str("DeviceID"),
                        Class = r.Str("PNPClass"),
                        ErrorCode = code,
                        Explanation = ErrorCodes.TryGetValue(code, out var text) ? text : "Erro não documentado; veja no Gerenciador de Dispositivos.",
                        DeviceId = r.Str("DeviceID"),
                    };
                })
                // Desconectados (45) não são problema
                .Where(p => p.ErrorCode != 45)
                .OrderBy(p => p.IsDisabled)
                .ThenBy(p => p.Name)
                .ToList();

        public static IReadOnlyList<DriverInfo> ListDrivers() =>
            Wmi.Query("SELECT DeviceName, DeviceClass, DriverProviderName, DriverVersion, DriverDate, IsSigned FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL")
                .Select(r => new DriverInfo
                {
                    Device = r.Str("DeviceName"),
                    Class = r.Str("DeviceClass"),
                    Provider = r.Str("DriverProviderName"),
                    Version = r.Str("DriverVersion"),
                    Date = r.Date("DriverDate"),
                    IsSigned = r.Bool("IsSigned") ?? false,
                })
                .GroupBy(d => d.Device + "|" + d.Version)
                .Select(g => g.First())
                .OrderBy(d => d.Date ?? DateTime.MaxValue)
                .ToList();
    }
}
