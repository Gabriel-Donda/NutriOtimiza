using System;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using OtimizaPC.Core;
using OtimizaPC.Tweaks.Registry;

namespace OtimizaPC.Tweaks.SystemTools
{
    /// <summary>
    /// Ativa o plano "Desempenho Máximo" (Windows 10 1803+) ou "Alto desempenho" (versões anteriores).
    /// Ao reverter, volta ao plano que estava ativo antes.
    /// </summary>
    public sealed class PowerPlanTweak : Tweak
    {
        private const string UltimateTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

        // GUID fixo para a cópia do "Desempenho Máximo", para reconhecê-la depois
        private const string OwnUltimate = "4f7c0de1-0f1a-4c3b-9d4e-0f7a5e1a0001";
        private const string PreviousKey = "previousScheme";

        private static readonly Regex GuidPattern = new Regex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

        public PowerPlanTweak()
            : base("power-plan", "Plano de energia de desempenho máximo",
                "Mantém o processador sempre pronto, sem economia de energia. Bom para desktops; em notebooks na bateria, reduz bastante a autonomia.",
                TweakCategories.Performance)
        {
            Risk = TweakRisk.Moderate;
        }

        private static bool UltimateAvailable => OsInfo.Build >= OsInfo.Windows10_1803;

        public override TweakState GetState()
        {
            var active = ActiveScheme();
            if (active == null) return TweakState.Unknown;
            return active.Equals(OwnUltimate, StringComparison.OrdinalIgnoreCase)
                   || active.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase)
                ? TweakState.Applied
                : TweakState.NotApplied;
        }

        public override void Prepare(UndoRecord record)
        {
            if (!record.Extra.ContainsKey(PreviousKey))
                record.Extra[PreviousKey] = ActiveScheme() ?? Balanced;
        }

        public override void Apply(UndoRecord record)
        {
            if (UltimateAvailable)
            {
                // Cria a cópia na primeira vez; se já existir, o setactive basta
                if (!CommandRunner.Run("powercfg.exe", "/list").Output.ToLowerInvariant().Contains(OwnUltimate))
                    CommandRunner.RunChecked("powercfg.exe", $"/duplicatescheme {UltimateTemplate} {OwnUltimate}");
                CommandRunner.RunChecked("powercfg.exe", $"/setactive {OwnUltimate}");
            }
            else
            {
                CommandRunner.RunChecked("powercfg.exe", $"/setactive {HighPerformance}");
            }
        }

        public override void Revert(UndoRecord record)
        {
            var previous = record != null && record.Extra.TryGetValue(PreviousKey, out var guid) ? guid : Balanced;
            if (previous.Equals(OwnUltimate, StringComparison.OrdinalIgnoreCase)) previous = Balanced;
            CommandRunner.RunChecked("powercfg.exe", $"/setactive {previous}");
        }

        private static string ActiveScheme()
        {
            try
            {
                var match = GuidPattern.Match(CommandRunner.Run("powercfg.exe", "/getactivescheme").Output);
                return match.Success ? match.Value.ToLowerInvariant() : null;
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível ler o plano de energia: " + ex.Message);
                return null;
            }
        }
    }

    /// <summary>Desativa a hibernação (libera o hiberfil.sys, do tamanho de boa parte da RAM).</summary>
    public sealed class HibernationTweak : Tweak
    {
        public HibernationTweak()
            : base("hibernation", "Desativar hibernação",
                "Libera no disco o espaço do arquivo hiberfil.sys (geralmente de 40% a 75% da RAM). Desativa também a Inicialização Rápida. Em notebooks, você perde a opção Hibernar.",
                TweakCategories.Performance)
        {
            Risk = TweakRisk.Moderate;
        }

        public override TweakState GetState()
        {
            using (var key = RegistryAccess.Open(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", false))
            {
                var value = key?.GetValue("HibernateEnabled");
                if (!(value is int enabled)) return TweakState.Unknown;
                return enabled == 0 ? TweakState.Applied : TweakState.NotApplied;
            }
        }

        public override void Apply(UndoRecord record) => CommandRunner.RunChecked("powercfg.exe", "/hibernate off");

        public override void Revert(UndoRecord record) => CommandRunner.RunChecked("powercfg.exe", "/hibernate on");
    }
}
