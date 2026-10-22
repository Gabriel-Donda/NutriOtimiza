using OtimizaPC.Core;

namespace OtimizaPC.Tweaks
{
    public enum TweakRisk
    {
        /// <summary>Sem efeito colateral relevante.</summary>
        Safe,

        /// <summary>Tem um custo que o usuário precisa aceitar (ex.: perder a hibernação).</summary>
        Moderate,

        /// <summary>Pode quebrar uma função usada por alguns (ex.: impressão).</summary>
        Advanced,
    }

    public enum RestartRequirement
    {
        None,
        Explorer,
        Logoff,
        Reboot,
    }

    public enum TweakState
    {
        NotApplied,
        Applied,
        Partial,
        Unknown,
    }

    public static class TweakCategories
    {
        public const string Privacy = "Privacidade";
        public const string Ads = "Anúncios e sugestões";
        public const string Interface = "Explorador e interface";
        public const string Gaming = "Jogos";
        public const string Performance = "Energia e desempenho";
        public const string Updates = "Atualizações e rede";
        public const string Services = "Serviços";
        public const string Security = "Segurança";

        public static readonly string[] Order = { Performance, Privacy, Ads, Interface, Gaming, Updates, Services, Security };
    }

    /// <summary>
    /// Uma otimização reversível. O fluxo é: <see cref="Prepare"/> grava o estado original no registro de desfazer,
    /// o registro é salvo em disco, e só então <see cref="Apply"/> altera o sistema.
    /// </summary>
    public abstract class Tweak
    {
        protected Tweak(string id, string name, string description, string category)
        {
            Id = id;
            Name = name;
            Description = description;
            Category = category;
        }

        public string Id { get; }

        public string Name { get; }

        public string Description { get; }

        public string Category { get; }

        public TweakRisk Risk { get; set; } = TweakRisk.Safe;

        public RestartRequirement Restart { get; set; } = RestartRequirement.None;

        /// <summary>Entra no botão "Recomendados".</summary>
        public bool Recommended { get; set; }

        public int MinBuild { get; set; }

        public int MaxBuild { get; set; } = int.MaxValue;

        /// <summary>Não se aplica ao Windows Server (ex.: ajustes de jogos).</summary>
        public bool ClientOnly { get; set; }

        public virtual bool RequiresAdmin => true;

        public bool IsSupported => OsInfo.Build >= MinBuild && OsInfo.Build <= MaxBuild && !(ClientOnly && OsInfo.IsServer);

        public abstract TweakState GetState();

        /// <summary>Guarda no registro de desfazer tudo o que <see cref="Apply"/> vai alterar. Não altera o sistema.</summary>
        public virtual void Prepare(UndoRecord record)
        {
        }

        public abstract void Apply(UndoRecord record);

        /// <summary>Volta ao estado original. Sem registro (aplicado fora do app), volta ao padrão do Windows.</summary>
        public abstract void Revert(UndoRecord record);

        public override string ToString() => Id;
    }
}
