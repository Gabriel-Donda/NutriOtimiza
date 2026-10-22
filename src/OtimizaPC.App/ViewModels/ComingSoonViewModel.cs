using System.Collections.Generic;
using Wpf.Ui.Controls;

namespace OtimizaPC.App.ViewModels
{
    /// <summary>Página de módulo ainda não implementado, listando o que vem nele.</summary>
    public sealed class ComingSoonViewModel
    {
        public ComingSoonViewModel(string title, SymbolRegular icon, int phase, string description, params string[] features)
        {
            Title = title;
            Icon = icon;
            Description = description;
            PhaseText = $"Chegando na Fase {phase}";
            Features = features;
        }

        public string Title { get; }

        public SymbolRegular Icon { get; }

        public string Description { get; }

        public string PhaseText { get; }

        public IReadOnlyList<string> Features { get; }
    }
}
