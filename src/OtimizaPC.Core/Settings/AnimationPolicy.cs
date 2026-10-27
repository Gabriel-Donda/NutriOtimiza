namespace OtimizaPC.Core.Settings
{
    /// <summary>
    /// Regras de quando as animações podem rodar. Fica no Core, sem WPF, para ser testável.
    /// O <c>MotionService</c> do App consulta estas regras e fornece os valores do sistema.
    /// </summary>
    public static class AnimationPolicy
    {
        /// <summary>Texto mostrado em Configurações quando as animações estão rodando.</summary>
        public const string ActiveText = "Animações ativas";

        /// <summary>Motivo automático para desligar as animações, ou null se o sistema as libera.</summary>
        /// <param name="forceAnimations">"Forçar animações": ignora só o bloqueio de hardware, nunca o do Windows nem o remoto.</param>
        public static string SystemBlockReason(bool clientAreaAnimation, bool remoteSession, bool weakHardware, bool forceAnimations = false)
        {
            if (!clientAreaAnimation) return "efeitos de animação desligados no Windows";
            if (remoteSession) return "acesso remoto (área de trabalho remota)";
            if (weakHardware && !forceAnimations) return "computador sem aceleração gráfica (renderização por software)";
            return null;
        }

        /// <summary>
        /// PC fraco: só quando o WPF renderiza por software (RenderCapability.Tier 0).
        /// O número de processadores não entra: placas integradas com Tier 1/2 animam bem.
        /// </summary>
        public static bool IsWeakHardware(int renderTier) => (renderTier >> 16) == 0;

        /// <summary>Animações ativas: o usuário quer e o sistema libera.</summary>
        public static bool IsEnabled(bool userEnabled, string systemBlockReason) =>
            userEnabled && systemBlockReason == null;

        /// <summary>Estado para o usuário: "Animações ativas" ou "Desativadas: motivo".</summary>
        public static string StatusText(bool userEnabled, string systemBlockReason)
        {
            if (!userEnabled) return "Desativadas: desligadas nesta tela";
            return systemBlockReason == null ? ActiveText : "Desativadas: " + systemBlockReason;
        }

        /// <summary>Linha de log com o estado e os valores usados na decisão.</summary>
        public static string LogLine(bool userEnabled, string systemBlockReason, int renderTier, int processorCount,
            bool clientAreaAnimation, bool remoteSession, bool forceAnimations)
        {
            var state = IsEnabled(userEnabled, systemBlockReason) ? "ativas" : StatusText(userEnabled, systemBlockReason);
            return $"Animações: {state} (RenderTier={renderTier >> 16}, ProcessorCount={processorCount}, " +
                $"ClientAreaAnimation={clientAreaAnimation}, Remoto={remoteSession}, Forçar={forceAnimations})";
        }
    }
}
