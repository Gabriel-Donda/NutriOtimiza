using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OtimizaPC.Core;
using OtimizaPC.Repair.Console;
using OtimizaPC.Repair.Tools;

namespace OtimizaPC.Repair.Fixes
{
    /// <summary>
    /// Tipo da ação de reparo para uma tela azul.
    /// </summary>
    public enum CrashFixKind
    {
        /// <summary>Executa SFC /scannow para verificar e reparar arquivos do sistema.</summary>
        Sfc,
        /// <summary>Executa DISM /RestoreHealth para reparar a imagem do Windows.</summary>
        Dism,
        /// <summary>Abre o Diagnóstico de Memória do Windows (mdsched.exe) — requer reinício.</summary>
        MemoryDiagnostic,
        /// <summary>Redefine os componentes do Windows Update.</summary>
        WindowsUpdateReset,
        /// <summary>Orienta o usuário a ir à aba Dispositivos e drivers para atualizar o driver.</summary>
        DriverGuidance,
    }

    /// <summary>
    /// Uma correção mapeada para um stop code específico, com risco, confirmação e execução assíncrona —
    /// espelhando exatamente o padrão de <see cref="EventFix"/>.
    /// </summary>
    public sealed class CrashFix
    {
        private readonly Func<Action<OutputLine>, CancellationToken, Task<RepairResult>> _run;

        internal CrashFix(
            string id,
            string title,
            string description,
            string risk,
            FixRisk riskLevel,
            bool requiresAdmin,
            bool requiresRestart,
            bool needsConfirmation,
            CrashFixKind kind,
            string guidance,
            Func<Action<OutputLine>, CancellationToken, Task<RepairResult>> run)
        {
            Id = id;
            Title = title;
            Description = description;
            Risk = risk;
            RiskLevel = riskLevel;
            RequiresAdmin = requiresAdmin;
            RequiresRestart = requiresRestart;
            NeedsConfirmation = riskLevel == FixRisk.Medium || needsConfirmation;
            Kind = kind;
            Guidance = guidance;
            _run = run;
        }

        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public string Risk { get; }
        public FixRisk RiskLevel { get; }
        public bool RequiresAdmin { get; }
        public bool RequiresRestart { get; }
        public bool NeedsConfirmation { get; }
        public CrashFixKind Kind { get; }

        /// <summary>
        /// Texto de orientação para correções sem execução automática (ex.: atualizar driver).
        /// Null quando há execução automática via RunAsync.
        /// </summary>
        public string Guidance { get; }

        /// <summary>True quando a correção é apenas uma orientação ao usuário (sem execução automática).</summary>
        public bool IsGuidanceOnly => _run == null;

        public Task<RepairResult> RunAsync(Action<OutputLine> output, CancellationToken ct) =>
            RunAsync(output, ct, false);

        public Task<RepairResult> RunAsync(Action<OutputLine> output, CancellationToken ct, bool confirmationGranted)
        {
            if (IsGuidanceOnly)
                throw new InvalidOperationException("Esta correção é apenas orientação; não há execução automática.");
            if (output == null) throw new ArgumentNullException(nameof(output));
            ct.ThrowIfCancellationRequested();
            if (RequiresAdmin && !AppInfo.IsAdministrator)
                throw new UnauthorizedAccessException("Este reparo exige execução como administrador.");
            if (NeedsConfirmation && !confirmationGranted)
                throw new InvalidOperationException("Este reparo exige confirmação explícita antes da execução.");
            return _run(output, ct);
        }
    }

    /// <summary>
    /// Mapeia stop codes (telas azuis) para ações seguras usando ferramentas já existentes no repositório.
    /// Padrão idêntico ao <see cref="EventFixCatalog"/> — sem inventar lógica nova de baixo nível.
    /// </summary>
    public static class CrashFixCatalog
    {
        // ── Grupos de stop codes por categoria ──────────────────────────────────

        /// <summary>
        /// Codes tipicamente causados por arquivos do sistema corrompidos → SFC + DISM.
        /// </summary>
        private static readonly HashSet<string> SystemFileCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0x000000EF", // CRITICAL_PROCESS_DIED
            "0xC000021A", // STATUS_SYSTEM_PROCESS_TERMINATED
            "0x0000007B", // INACCESSIBLE_BOOT_DEVICE
            "0x00000139", // KERNEL_SECURITY_CHECK_FAILURE
            "0x0000003B", // SYSTEM_SERVICE_EXCEPTION
        };

        /// <summary>
        /// Codes tipicamente causados por RAM com defeito → Diagnóstico de Memória do Windows.
        /// </summary>
        private static readonly HashSet<string> MemoryCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0x0000001A", // MEMORY_MANAGEMENT
            "0x0000004E", // PFN_LIST_CORRUPT
            "0x00000050", // PAGE_FAULT_IN_NONPAGED_AREA
            "0x00000019", // BAD_POOL_HEADER
            "0x000000C2", // BAD_POOL_CALLER
            "0x0000007F", // UNEXPECTED_KERNEL_MODE_TRAP
            "0x00000124", // WHEA_UNCORRECTABLE_ERROR
        };

        /// <summary>
        /// Codes tipicamente causados por driver (vídeo, rede, armazenamento) → orientar aba Drivers.
        /// </summary>
        private static readonly HashSet<string> DriverCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0x000000D1", // DRIVER_IRQL_NOT_LESS_OR_EQUAL
            "0x0000007E", // SYSTEM_THREAD_EXCEPTION_NOT_HANDLED
            "0x00000116", // VIDEO_TDR_FAILURE
            "0x00000117", // VIDEO_TDR_TIMEOUT_DETECTED
            "0x00000119", // VIDEO_SCHEDULER_INTERNAL_ERROR
            "0x0000009F", // DRIVER_POWER_STATE_FAILURE
            "0x000000A0", // INTERNAL_POWER_ERROR
            "0x000000C4", // DRIVER_VERIFIER_DETECTED_VIOLATION
            "0x0000013A", // KERNEL_MODE_HEAP_CORRUPTION
            "0x0000001E", // KMODE_EXCEPTION_NOT_HANDLED
            "0x0000000A", // IRQL_NOT_LESS_OR_EQUAL
        };

        /// <summary>
        /// Codes ligados a armazenamento/Windows Update (SSD com firmware desatualizado etc.)
        /// → reset do Windows Update + orientação de driver.
        /// </summary>
        private static readonly HashSet<string> StorageCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0x00000133", // DPC_WATCHDOG_VIOLATION
            "0x00000154", // UNEXPECTED_STORE_EXCEPTION
            "0x0000007A", // KERNEL_DATA_INPAGE_ERROR
            "0x000000F4", // CRITICAL_OBJECT_TERMINATION
        };

        // ── API pública ──────────────────────────────────────────────────────────

        /// <summary>
        /// Retorna a lista de <see cref="CrashFix"/> aplicáveis ao stop code fornecido.
        /// Aceita o código no formato "0x0000009F" (como devolvido por <c>CrashRecord.Code</c>).
        /// Retorna lista vazia para desligamentos sem tela azul (code == null) ou codes desconhecidos.
        /// </summary>
        public static IReadOnlyList<CrashFix> For(string stopCode)
        {
            if (string.IsNullOrWhiteSpace(stopCode)) return Array.Empty<CrashFix>();

            var normalized = Normalize(stopCode);
            var fixes = new List<CrashFix>();

            if (SystemFileCodes.Contains(normalized))
            {
                fixes.Add(SfcFix());
                fixes.Add(DismFix());
            }
            else if (MemoryCodes.Contains(normalized))
            {
                fixes.Add(MemoryDiagFix());
            }
            else if (StorageCodes.Contains(normalized))
            {
                fixes.Add(SfcFix());
                fixes.Add(WuResetFix());
                fixes.Add(DriverGuidanceFix(
                    "storage-driver-guidance",
                    "Verificar driver de armazenamento (SSD/HDD)",
                    "Drivers de controladora SATA/NVMe desatualizados causam este stop code. Vá à aba \"Dispositivos e drivers\" para identificar e atualizar o driver de armazenamento."));
            }
            else if (DriverCodes.Contains(normalized))
            {
                fixes.Add(DriverGuidanceFix(
                    "driver-guidance-" + normalized.ToLowerInvariant(),
                    "Atualizar ou reverter driver",
                    "Este stop code é causado tipicamente por um driver com defeito (vídeo, rede, Wi-Fi ou chipset). " +
                    "Vá à aba \"Dispositivos e drivers\" para identificar o driver suspeito e atualizá-lo ou revertê-lo."));
            }

            return fixes;
        }

        /// <summary>
        /// Verifica se existe pelo menos uma correção mapeada para o stop code.
        /// </summary>
        public static bool HasFix(string stopCode) => For(stopCode).Count > 0;

        // ── Fábricas internas ────────────────────────────────────────────────────

        private static CrashFix SfcFix()
        {
            var tool = new CommandTool(
                "crash-sfc",
                "Verificar arquivos do sistema (SFC)",
                "Escaneia e tenta reparar arquivos do Windows protegidos. Não apaga dados do usuário.",
                RepairGroups.Windows,
                new[] { new CommandStep("sfc.exe", "/scannow") });
            tool.RequiresAdmin = true;
            tool.NeedsConfirmation = true;
            return new CrashFix(
                "crash-sfc",
                "Verificar arquivos do sistema (SFC)",
                "Escaneia e tenta reparar arquivos do Windows protegidos. Não apaga dados do usuário.",
                "médio — pode substituir arquivos protegidos do Windows",
                FixRisk.Medium,
                requiresAdmin: true,
                requiresRestart: false,
                needsConfirmation: true,
                CrashFixKind.Sfc,
                guidance: null,
                run: (output, ct) => tool.RunAsync(output, ct));
        }

        private static CrashFix DismFix()
        {
            var tool = new CommandTool(
                "crash-dism",
                "Reparar imagem do Windows (DISM)",
                "Repara o repositório de componentes do Windows. Pode baixar arquivos da Microsoft.",
                RepairGroups.Windows,
                new[] { new CommandStep("dism.exe", "/Online /Cleanup-Image /RestoreHealth") });
            tool.RequiresAdmin = true;
            tool.NeedsConfirmation = true;
            return new CrashFix(
                "crash-dism",
                "Reparar imagem do Windows (DISM)",
                "Repara o repositório de componentes do Windows. Pode baixar arquivos da Microsoft.",
                "médio — pode baixar e substituir componentes do Windows",
                FixRisk.Medium,
                requiresAdmin: true,
                requiresRestart: false,
                needsConfirmation: true,
                CrashFixKind.Dism,
                guidance: null,
                run: (output, ct) => tool.RunAsync(output, ct));
        }

        private static CrashFix MemoryDiagFix()
        {
            // mdsched.exe é uma ferramenta interativa do Windows que agenda o diagnóstico para o próximo boot.
            // É orientação interativa: informa o usuário e abre a ferramenta — não executa nada destrutivo.
            return new CrashFix(
                "crash-memdiag",
                "Abrir Diagnóstico de Memória do Windows",
                "Abre a ferramenta de diagnóstico de RAM do Windows. Você escolhe se quer reiniciar agora ou na próxima vez.",
                "baixo — apenas abre a ferramenta oficial do Windows; você decide quando reiniciar",
                FixRisk.Low,
                requiresAdmin: false,
                requiresRestart: false,   // o usuário decide dentro da ferramenta
                needsConfirmation: false,
                CrashFixKind.MemoryDiagnostic,
                guidance: null,
                run: (output, ct) =>
                {
                    output(new OutputLine("Abrindo o Diagnóstico de Memória do Windows (mdsched.exe)…", false));
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mdsched.exe")
                        {
                            UseShellExecute = true,
                        });
                        output(new OutputLine("Ferramenta aberta. Escolha quando deseja reiniciar para executar o diagnóstico.", false));
                        History.Record("Reparo", "Diagnóstico de Memória do Windows aberto.");
                        return Task.FromResult(new RepairResult { Success = true, Summary = "Diagnóstico de Memória do Windows aberto com sucesso." });
                    }
                    catch (Exception ex)
                    {
                        output(new OutputLine("Não foi possível abrir mdsched.exe: " + ex.Message, false, true));
                        return Task.FromResult(new RepairResult { Success = false, Summary = "Erro ao abrir o Diagnóstico de Memória: " + ex.Message });
                    }
                });
        }

        private static CrashFix WuResetFix()
        {
            var tool = new WindowsUpdateResetTool();
            return new CrashFix(
                "crash-wu-reset",
                "Redefinir componentes do Windows Update",
                "Para os serviços de atualização e renomeia os caches para que o Windows os recrie. " +
                "Drivers de armazenamento com firmware desatualizado costumam falhar na fase de atualização.",
                "médio — interrompe serviços de atualização temporariamente",
                FixRisk.Medium,
                requiresAdmin: true,
                requiresRestart: false,
                needsConfirmation: true,
                CrashFixKind.WindowsUpdateReset,
                guidance: null,
                run: (output, ct) => tool.RunAsync(output, ct));
        }

        private static CrashFix DriverGuidanceFix(string id, string title, string guidance)
        {
            return new CrashFix(
                id,
                title,
                guidance,
                "nenhum — apenas orienta; nenhum arquivo é alterado",
                FixRisk.None,
                requiresAdmin: false,
                requiresRestart: false,
                needsConfirmation: false,
                CrashFixKind.DriverGuidance,
                guidance: guidance,
                run: null);   // IsGuidanceOnly = true
        }

        // ── Utilitários ──────────────────────────────────────────────────────────

        /// <summary>
        /// Normaliza o stop code para o formato "0x000000EF" (8 dígitos hex maiúsculos).
        /// Aceita "0x000000ef", "0xEF", "0x000000EF" etc.
        /// </summary>
        internal static string Normalize(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            var raw = code.Trim();
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring(2);
            if (ulong.TryParse(raw, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return "0x" + ((uint)value).ToString("X8");
            }
            return code.Trim().ToUpperInvariant();
        }
    }
}
