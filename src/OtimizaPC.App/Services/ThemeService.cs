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
            resources["BrandTitleBrush"] = new SolidColorBrush(theme == ApplicationTheme.Light ? BrandGraphite : BrandGold);
        }

        /// <summary>Só acompanha a troca de tema do Windows quando a preferência é "Sistema".</summary>
        public static void Watch(ThemePreference preference)
        {
            if (!(Application.Current?.MainWindow is Window window)) return;
            try
            {
                SystemThemeWatcher.UnWatch(window);
                if (preference != ThemePreference.System) return;

                SystemThemeWatcher.Watch(window, WindowBackdropType.None, updateAccents: false);
                // Ao trocar o tema do Windows, reaplica as cores da marca para o novo tema
                ApplicationThemeManager.Changed -= OnThemeChanged;
                ApplicationThemeManager.Changed += OnThemeChanged;
            }
            catch (Exception ex)
            {
                Log.Warn("Monitor de tema indisponível: " + ex.Message);
            }
        }

        private static void OnThemeChanged(ApplicationTheme theme, Color accent) => ApplyBrandColors();

        /// <summary>Clareia a cor misturando com branco (usado nos estados de hover/pressionado).</summary>
        private static Color Shift(Color color, double amount) => Color.FromRgb(
            (byte)(color.R + (255 - color.R) * amount),
            (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));
    }
}
