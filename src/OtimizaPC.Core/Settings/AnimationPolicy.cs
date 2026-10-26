namespace OtimizaPC.Core.Settings
{
    /// <summary>
    /// Regras de quando as animações podem rodar. Fica no Core, sem WPF, para ser testável.
    /// O <c>MotionService</c> do App consulta estas regras e fornece os valores do sistema.
    /// </summary>
    public static class AnimationPolicy
    {
        /// <summary>Limite de versão do mecanismo de renderização abaixo do qual o PC é tratado como fraco (Tier 2 = 0x20000).</summary>
        public const int MinimumRenderTier = 0x20000;

        /// <summary>Motivo automático para desligar as animações, ou null se o sistema as libera.</summary>
        public static string SystemBlockReason(bool clientAreaAnimation, bool remoteSession, bool weakHardware)
        {
            if (!clientAreaAnimation) return "Desativadas pelo Windows (efeitos de animação)";
            if (remoteSession) return "Desativadas durante o acesso remoto";
            if (weakHardware) return "Desativadas: computador com pouco recurso gráfico";
            return null;
        }

        /// <summary>PC fraco: renderização abaixo do Tier 2 ou dois núcleos ou menos.</summary>
        public static bool IsWeakHardware(int renderTier, int processorCount) =>
            renderTier < MinimumRenderTier || processorCount <= 2;

        /// <summary>Animações ativas: o usuário quer e o sistema libera.</summary>
        public static bool IsEnabled(bool userEnabled, string systemBlockReason) =>
            userEnabled && systemBlockReason == null;
    }
}
