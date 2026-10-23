using System;
using System.Windows;
using System.Windows.Media;
using OtimizaPC.Core;
using OtimizaPC.Core.Settings;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace OtimizaPC.App.Services
{
    /// <summary>
    /// Aplica o tema escolhido nas configurações com a identidade visual da Nutriport:
    /// laranja como cor de destaque e dourado nos títulos (no tema escuro).
    /// </summary>
    public static class ThemeService
    {
        public static readonly Color BrandOrange = Color.FromRgb(0xF5, 0x86, 0x34);
        public static readonly Color BrandGold = Color.FromRgb(0xD2, 0xC0, 0x68);
        public static readonly Color BrandGraphite = Color.FromRgb(0x20, 0x20, 0x20);

        public static void Apply(ThemePreference preference)
        {
            try
            {
                switch (preference)
                {
                    case ThemePreference.Light:
                        ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.None, updateAccent: false);
                        break;
                    case ThemePreference.Dark:
                        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.None, updateAccent: false);
                        break;
                    default:
                        // Windows 8.1 / Server 2012 R2 não têm tema do sistema: fica o padrão do WPF-UI
                        ApplicationThemeManager.ApplySystemTheme(updateAccent: false);
                        break;
                }
                ApplyBrandColors();
                Watch(preference);
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível aplicar o tema: " + ex.Message);
            }
        }

        /// <summary>Laranja da marca como destaque, no lugar da cor de destaque do Windows.</summary>
        public static void ApplyBrandColors()
        {
            var theme = ApplicationThemeManager.GetAppTheme();
            // Variações explícitas: com Apply(cor, tema) o WPF-UI clareia o destaque no tema escuro e o laranja vira pêssego
            ApplicationAccentColorManager.Apply(BrandOrange, BrandOrange, Shift(BrandOrange, 0.10), Shift(BrandOrange, 0.20));

            // Dourado sobre fundo claro fica ilegível: no tema claro os títulos usam grafite
            var resources = Application.Current.Resources;
            var isLight = theme == ApplicationTheme.Light;
            resources["BrandTitleBrush"] = new SolidColorBrush(isLight ? BrandGraphite : BrandGold);
            ApplySemanticResources(resources, isLight);
        }

        /// <summary>Só acompanha a troca de tema do Windows quando a preferência é "Sistema".</summary>
        public static void Watch(ThemePreference preference)
        {
            if (!(Application.Current?.MainWindow is Window window)) return;
            try
            {
                // Preferências fixas não devem deixar um handler antigo observando o tema do Windows.
                ApplicationThemeManager.Changed -= OnThemeChanged;
                SystemThemeWatcher.UnWatch(window);
                if (preference != ThemePreference.System) return;

                SystemThemeWatcher.Watch(window, WindowBackdropType.None, updateAccents: false);
                // Ao trocar o tema do Windows, reaplica as cores da marca para o novo tema
                ApplicationThemeManager.Changed += OnThemeChanged;
            }
            catch (Exception ex)
            {
                Log.Warn("Monitor de tema indisponível: " + ex.Message);
            }
        }

        private static void OnThemeChanged(ApplicationTheme theme, Color accent) => ApplyBrandColors();

        /// <summary>Atualiza os tokens próprios do app depois que o WPF-UI troca o tema.</summary>
        private static void ApplySemanticResources(ResourceDictionary resources, bool isLight)
        {
            SetBrush(resources, "SurfaceNavigationBrush", isLight ? "#FFF7F7F7" : "#FF202020");
            SetBrush(resources, "SurfaceNavigationHoverBrush", isLight ? "#FFEDEDED" : "#FF2C2C2C");
            SetBrush(resources, "SurfaceNavigationSelectedBrush", isLight ? "#FFFFE8D6" : "#FF3A3027");
            SetBrush(resources, "NavigationSelectedForegroundBrush", isLight ? "#FF7A3E12" : "#FFFFF4D0");
            SetBrush(resources, "FocusStrokeBrush", isLight ? "#FFB8541A" : "#FFF58634");
            SetBrush(resources, "StatusWarningForegroundBrush", isLight ? "#FF7A4B00" : "#FFF8D477");
            SetBrush(resources, "StatusCriticalForegroundBrush", isLight ? "#FFB42318" : "#FFFFB4AB");
            SetBrush(resources, "StatusWarningOnFillBrush", isLight ? "#FF4A2A00" : "#FF1F1F1F");
            SetBrush(resources, "StatusCriticalOnFillBrush", "#FFFFFFFF");
            SetBrush(resources, "LogBackgroundBrush", isLight ? "#FFF4F4F4" : "#FF101010");
            SetBrush(resources, "LogForegroundBrush", isLight ? "#FF242424" : "#FFD4D4D4");
            SetBrush(resources, "LogErrorForegroundBrush", isLight ? "#FFB42318" : "#FFF48771");
        }

        private static void SetBrush(ResourceDictionary resources, string key, string color)
        {
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        /// <summary>Clareia a cor misturando com branco (usado nos estados de hover/pressionado).</summary>
        private static Color Shift(Color color, double amount) => Color.FromRgb(
            (byte)(color.R + (255 - color.R) * amount),
            (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));
    }
}
