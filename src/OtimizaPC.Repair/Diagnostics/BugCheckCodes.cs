using System.Collections.Generic;
using System.Globalization;

namespace OtimizaPC.Repair.Diagnostics
{
    /// <summary>Códigos de parada (tela azul) mais comuns, com a causa típica em linguagem simples.</summary>
    public static class BugCheckCodes
    {
        private static readonly Dictionary<uint, (string Name, string Explanation)> Codes = new Dictionary<uint, (string, string)>
        {
            [0x0A] = ("IRQL_NOT_LESS_OR_EQUAL", "Um driver acessou memória de forma inválida. Atualize drivers (rede, vídeo, chipset); se persistir, teste a RAM."),
            [0x19] = ("BAD_POOL_HEADER", "Memória do kernel corrompida, geralmente por um driver ou antivírus."),
            [0x1A] = ("MEMORY_MANAGEMENT", "Erro no gerenciamento de memória: RAM com defeito ou driver. Rode o Diagnóstico de Memória."),
            [0x1E] = ("KMODE_EXCEPTION_NOT_HANDLED", "Um driver gerou um erro que o Windows não conseguiu tratar. Atualize drivers recentes."),
            [0x3B] = ("SYSTEM_SERVICE_EXCEPTION", "Erro em um driver ou serviço do sistema (frequentemente vídeo ou antivírus)."),
            [0x4E] = ("PFN_LIST_CORRUPT", "Corrupção na lista de memória física: costuma ser RAM com defeito."),
            [0x50] = ("PAGE_FAULT_IN_NONPAGED_AREA", "Acesso a memória inexistente: driver com defeito, antivírus ou RAM."),
            [0x7A] = ("KERNEL_DATA_INPAGE_ERROR", "O Windows não conseguiu ler dados do disco: disco, cabo SATA ou controladora com problema."),
            [0x7B] = ("INACCESSIBLE_BOOT_DEVICE", "O Windows não encontrou o disco de boot: modo SATA (AHCI/RAID) alterado na BIOS ou disco falhando."),
            [0x7E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "Um driver travou o sistema. Atualize ou reverta o driver instalado mais recentemente."),
            [0x7F] = ("UNEXPECTED_KERNEL_MODE_TRAP", "Falha de hardware ou driver; verifique overclock, temperatura e RAM."),
            [0x9F] = ("DRIVER_POWER_STATE_FAILURE", "Um driver não respondeu ao suspender/hibernar. Atualize drivers de rede, vídeo e chipset."),
            [0xA0] = ("INTERNAL_POWER_ERROR", "Erro no gerenciamento de energia (hibernação/suspensão)."),
            [0xC2] = ("BAD_POOL_CALLER", "Um driver usou memória do kernel de forma incorreta."),
            [0xC4] = ("DRIVER_VERIFIER_DETECTED_VIOLATION", "O Verificador de Drivers detectou um driver com defeito (estava ativado para diagnóstico)."),
            [0xD1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", "Driver com defeito, muito comum em placas de rede/Wi-Fi e antivírus."),
            [0xEF] = ("CRITICAL_PROCESS_DIED", "Um processo essencial do Windows parou: arquivos do sistema corrompidos ou disco com problema. Rode o SFC e o DISM."),
            [0xF4] = ("CRITICAL_OBJECT_TERMINATION", "Um processo essencial foi encerrado; frequentemente disco ou cabo com problema."),
            [0x101] = ("CLOCK_WATCHDOG_TIMEOUT", "Um núcleo do processador parou de responder: overclock, temperatura, BIOS desatualizada ou CPU com defeito."),
            [0x116] = ("VIDEO_TDR_FAILURE", "O driver de vídeo travou e não se recuperou: atualize/reinstale o driver, verifique temperatura e overclock da GPU."),
            [0x117] = ("VIDEO_TDR_TIMEOUT_DETECTED", "A placa de vídeo parou de responder."),
            [0x119] = ("VIDEO_SCHEDULER_INTERNAL_ERROR", "Erro no agendador da placa de vídeo; reinstale o driver de vídeo."),
            [0x124] = ("WHEA_UNCORRECTABLE_ERROR", "Erro de hardware não corrigível (CPU, RAM, placa-mãe ou fonte). Remova overclock e verifique temperatura e fonte."),
            [0x133] = ("DPC_WATCHDOG_VIOLATION", "Um driver demorou demais para responder; comum com drivers de SSD/armazenamento ou firmware de SSD desatualizado."),
            [0x139] = ("KERNEL_SECURITY_CHECK_FAILURE", "Dados do kernel corrompidos por driver incompatível ou RAM com defeito."),
            [0x13A] = ("KERNEL_MODE_HEAP_CORRUPTION", "Um driver corrompeu a memória do kernel (comum em drivers de vídeo)."),
            [0x154] = ("UNEXPECTED_STORE_EXCEPTION", "Erro ao acessar memória comprimida/disco: verifique o SSD e o driver de armazenamento."),
            [0x1D3] = ("DRIVER_INVALID_CRUNTIME_PARAMETER", "Um driver chamou uma função do kernel com parâmetro inválido."),
            [0xC000021A] = ("STATUS_SYSTEM_PROCESS_TERMINATED", "Um processo essencial (winlogon/csrss) falhou: arquivos do sistema corrompidos."),
        };

        public static (string Name, string Explanation) Describe(string hexCode)
        {
            if (hexCode == null || !ulong.TryParse(hexCode.Replace("0x", string.Empty), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                return ("Código desconhecido", null);

            var code = (uint)value;
            if (Codes.TryGetValue(code, out var known)) return known;

            // 0x1000007E, 0x1000008E...: variantes com o bit 28 ligado
            var variant = code & 0x0FFFFFFF;
            if (variant != code && Codes.TryGetValue(variant, out known)) return known;

            return ("Código " + hexCode, "Código menos comum. Pesquise o código ou abra o arquivo de despejo no WinDbg para identificar o driver.");
        }
    }
}
