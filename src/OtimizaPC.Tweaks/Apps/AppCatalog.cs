using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OtimizaPC.Tweaks.Apps
{
    public enum AppRecommendation
    {
        /// <summary>Propaganda, jogo pré-instalado ou app descontinuado.</summary>
        Remove,

        /// <summary>Útil para alguns: o usuário decide.</summary>
        Optional,
    }

    public sealed class AppCatalogEntry
    {
        public AppCatalogEntry(string pattern, string displayName, string description, AppRecommendation recommendation)
        {
            Pattern = pattern;
            DisplayName = displayName;
            Description = description;
            Recommendation = recommendation;
        }

        /// <summary>Nome do pacote; aceita * como curinga.</summary>
        public string Pattern { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public AppRecommendation Recommendation { get; }

        public bool Matches(string packageName) => AppCatalog.WildcardMatch(Pattern, packageName);
    }

    /// <summary>Bloatware conhecido e lista de pacotes que nunca devem ser removidos.</summary>
    public static class AppCatalog
    {
        private const AppRecommendation Remove = AppRecommendation.Remove;
        private const AppRecommendation Optional = AppRecommendation.Optional;

        public static readonly IReadOnlyList<AppCatalogEntry> Entries = new[]
        {
            // ---------- Recomendado remover ----------
            new AppCatalogEntry("king.com.*", "Jogos King (Candy Crush etc.)", "Jogos pré-instalados com propaganda e compras.", Remove),
            new AppCatalogEntry("Disney.*", "Disney+", "Atalho promocional pré-instalado.", Remove),
            new AppCatalogEntry("BytedancePte.Ltd.TikTok", "TikTok", "App promocional pré-instalado.", Remove),
            new AppCatalogEntry("Facebook.*", "Facebook / Instagram", "App promocional pré-instalado.", Remove),
            new AppCatalogEntry("AmazonVideo.PrimeVideo", "Prime Video", "Atalho promocional pré-instalado.", Remove),
            new AppCatalogEntry("*AmazonAlexa", "Amazon Alexa", "App promocional pré-instalado.", Remove),
            new AppCatalogEntry("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", "Paciência com propaganda e assinatura.", Remove),
            new AppCatalogEntry("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office)", "Atalho que só oferece a assinatura do Office. Não afeta o Office instalado.", Remove),
            new AppCatalogEntry("Microsoft.BingNews", "Notícias", "Agregador de notícias do Bing.", Remove),
            new AppCatalogEntry("Microsoft.BingSearch", "Pesquisa do Bing", "App de busca do Bing.", Remove),
            new AppCatalogEntry("Microsoft.BingFinance", "Finanças (Bing)", "App antigo do Windows 8/10.", Remove),
            new AppCatalogEntry("Microsoft.BingSports", "Esportes (Bing)", "App antigo do Windows 8/10.", Remove),
            new AppCatalogEntry("Microsoft.BingTravel", "Viagens (Bing)", "App antigo do Windows 8.", Remove),
            new AppCatalogEntry("Microsoft.BingFoodAndDrink", "Comidas e Bebidas (Bing)", "App antigo do Windows 8.", Remove),
            new AppCatalogEntry("Microsoft.BingHealthAndFitness", "Saúde e Bem-Estar (Bing)", "App antigo do Windows 8.", Remove),
            new AppCatalogEntry("Microsoft.Getstarted", "Dicas", "Tutoriais e sugestões do Windows.", Remove),
            new AppCatalogEntry("Microsoft.WindowsFeedbackHub", "Hub de Comentários", "Envio de feedback para a Microsoft.", Remove),
            new AppCatalogEntry("Microsoft.Microsoft3DViewer", "Visualizador 3D", "Visualizador de modelos 3D, descontinuado.", Remove),
            new AppCatalogEntry("Microsoft.Print3D", "Print 3D", "Descontinuado.", Remove),
            new AppCatalogEntry("Microsoft.MixedReality.Portal", "Portal de Realidade Misturada", "Só serve para óculos de realidade mista (descontinuados).", Remove),
            new AppCatalogEntry("Microsoft.SkypeApp", "Skype", "Serviço encerrado pela Microsoft em 2025.", Remove),
            new AppCatalogEntry("Microsoft.People", "Pessoas", "Descontinuado.", Remove),
            new AppCatalogEntry("Microsoft.Messaging", "Mensagens", "Descontinuado.", Remove),
            new AppCatalogEntry("Microsoft.OneConnect", "Planos Móveis", "Só para PCs com chip de celular.", Remove),
            new AppCatalogEntry("Microsoft.Wallet", "Carteira", "Descontinuado.", Remove),
            new AppCatalogEntry("Microsoft.549981C3F5F10", "Cortana", "Assistente descontinuado.", Remove),
            new AppCatalogEntry("Microsoft.Office.OneNote", "OneNote para Windows 10", "Versão antiga da Store; o OneNote do Office continua funcionando.", Remove),
            new AppCatalogEntry("Microsoft.Windows.DevHome", "Dev Home", "Ferramenta para desenvolvedores, descontinuada.", Remove),
            new AppCatalogEntry("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family", "Controle parental online (o recurso continua no site).", Remove),

            // ---------- Opcional ----------
            new AppCatalogEntry("Microsoft.BingWeather", "Clima", "Previsão do tempo.", Optional),
            new AppCatalogEntry("Microsoft.GetHelp", "Obter Ajuda", "Suporte e solução de problemas do Windows.", Optional),
            new AppCatalogEntry("Microsoft.YourPhone", "Vincular ao Celular", "Integra o PC ao celular (SMS, fotos, chamadas).", Optional),
            new AppCatalogEntry("Microsoft.WindowsMaps", "Mapas", "Mapas offline do Windows.", Optional),
            new AppCatalogEntry("Microsoft.ZuneVideo", "Filmes e TV", "Player de vídeo antigo.", Optional),
            new AppCatalogEntry("Microsoft.ZuneMusic", "Media Player / Groove", "No Windows 11 é o Media Player padrão.", Optional),
            new AppCatalogEntry("Microsoft.Todos", "Microsoft To Do", "Lista de tarefas.", Optional),
            new AppCatalogEntry("Microsoft.PowerAutomateDesktop", "Power Automate", "Automação de tarefas.", Optional),
            new AppCatalogEntry("Microsoft.WindowsAlarms", "Relógio", "Alarmes, cronômetro e foco.", Optional),
            new AppCatalogEntry("Microsoft.WindowsSoundRecorder", "Gravador de Som", "Gravação de áudio.", Optional),
            new AppCatalogEntry("Microsoft.MicrosoftStickyNotes", "Notas Autoadesivas", "Lembretes na área de trabalho.", Optional),
            new AppCatalogEntry("Microsoft.OutlookForWindows", "Outlook (novo)", "Novo cliente de e-mail; substitui o Email e Calendário.", Optional),
            new AppCatalogEntry("microsoft.windowscommunicationsapps", "Email e Calendário", "Cliente de e-mail antigo do Windows.", Optional),
            new AppCatalogEntry("MicrosoftTeams", "Teams (pessoal)", "Chat da barra de tarefas do Windows 11.", Optional),
            new AppCatalogEntry("MSTeams", "Microsoft Teams", "Remova só se não usa o Teams no trabalho.", Optional),
            new AppCatalogEntry("Microsoft.Copilot", "Copilot", "Assistente de IA da Microsoft.", Optional),
            new AppCatalogEntry("Clipchamp.Clipchamp", "Clipchamp", "Editor de vídeo online.", Optional),
            new AppCatalogEntry("SpotifyAB.SpotifyMusic", "Spotify", "Remova se veio pré-instalado e você não usa.", Optional),
            new AppCatalogEntry("*Netflix", "Netflix", "Remova se veio pré-instalado e você não usa.", Optional),
            new AppCatalogEntry("MicrosoftCorporationII.QuickAssist", "Assistência Rápida", "Acesso remoto da Microsoft, muito usado em golpes de \"suporte técnico\".", Optional),
            new AppCatalogEntry("MicrosoftWindows.Client.WebExperience", "Widgets", "Painel de notícias e clima da barra de tarefas.", Optional),
            new AppCatalogEntry("Microsoft.GamingApp", "Xbox", "Necessário para jogos do PC Game Pass.", Optional),
            new AppCatalogEntry("Microsoft.XboxApp", "Xbox Console Companion", "App antigo do Xbox.", Optional),
            new AppCatalogEntry("Microsoft.XboxGamingOverlay", "Xbox Game Bar", "Sobreposição de jogos (Win+G): gravação, desempenho, chat.", Optional),
            new AppCatalogEntry("Microsoft.XboxSpeechToTextOverlay", "Xbox Fala para Texto", "Legendas no chat de voz de jogos.", Optional),
        };

        // Pacotes essenciais: removê-los quebra vídeo, fotos, instalação de apps, segurança ou drivers
        private static readonly string[] Protected =
        {
            "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.Winget.*",
            "Microsoft.SecHealthUI", "Microsoft.WindowsTerminal", "Microsoft.WindowsNotepad", "Microsoft.Paint",
            "Microsoft.Windows.Photos", "Microsoft.WindowsCalculator", "Microsoft.ScreenSketch", "Microsoft.WindowsCamera",
            "Microsoft.MicrosoftEdge*", "Microsoft.Win32WebViewHost", "Microsoft.AAD.BrokerPlugin", "Microsoft.AccountsControl",
            "Microsoft.LockApp", "Microsoft.*Extension", "Microsoft.*Extensions", "Microsoft.VCLibs*", "Microsoft.UI.Xaml*",
            "Microsoft.NET.*", "Microsoft.Services.Store.Engagement", "Microsoft.LanguageExperiencePack*",
            "Microsoft.ApplicationCompatibilityEnhancements", "Microsoft.D3DMappingLayers", "Microsoft.WidgetsPlatformRuntime",
            "Microsoft.WindowsAppRuntime*", "MicrosoftCorporationII.WinAppRuntime*", "Microsoft.OneDriveSync",
            "Microsoft.XboxIdentityProvider", "Microsoft.Xbox.TCUI", "Microsoft.StartExperiencesApp",
            "Microsoft.Windows.*", "Windows.*", "MicrosoftWindows.*", "NcsiUwpApp", "Microsoft.CredDialogHost",
            // Painéis de driver (vídeo, áudio, armazenamento)
            "AppUp.*", "NVIDIACorp.NVIDIAControlPanel", "AdvancedMicroDevicesInc*", "RealtekSemiconductorCorp.*",
            "DolbyLaboratories.*", "*.WavesMaxxAudio*", "*.SynapticsUtilities", "*.ElanTouchpad*",
        };

        public static AppCatalogEntry Find(string packageName) => Entries.FirstOrDefault(e => e.Matches(packageName));

        /// <summary>Nunca oferecido para remoção, a não ser que esteja no catálogo curado.</summary>
        public static bool IsProtected(AppPackage package)
        {
            if (package.NonRemovable) return true;
            if (Find(package.Name) != null) return false;
            // Fora do catálogo, só apps da Store (não do sistema) e fora da lista de essenciais
            return !package.IsStore || Protected.Any(p => WildcardMatch(p, package.Name));
        }

        internal static bool WildcardMatch(string pattern, string value)
        {
            if (value == null) return false;
            var regex = "^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$";
            return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
