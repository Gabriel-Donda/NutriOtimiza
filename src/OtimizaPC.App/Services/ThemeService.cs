using System;
using System.Windows;
using OtimizaPC.Core;
using OtimizaPC.Core.Settings;
using Wpf.Ui.Appearance;

namespace OtimizaPC.App.Services
{
    /// <summary>Aplica o tema escolhido nas configurações (claro, escuro ou o do Windows).</summary>
    public static class ThemeService
    {
        public static void Apply(ThemePreference preference)
        {
            try
            {
                switch (preference)
                {
                    case ThemePreference.Light:
                        ApplicationThemeManager.Apply(ApplicationTheme.Light);
                        break;
                    case ThemePreference.Dark:
                        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                        break;
                    default:
                        // Windows 8.1 / Server 2012 R2 não têm tema do sistema: fica o padrão do WPF-UI
                        ApplicationThemeManager.ApplySystemTheme();
                        break;
                }
                Watch(preference);
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível aplicar o tema: " + ex.Message);
            }
        }

        /// <summary>Só acompanha a troca de tema do Windows quando a preferência é "Sistema".</summary>
        public static void Watch(ThemePreference preference)
        {
            if (!(Application.Current?.MainWindow is Window window)) return;
            try
            {
                SystemThemeWatcher.UnWatch(window);
                if (preference == ThemePreference.System) SystemThemeWatcher.Watch(window);
            }
            catch (Exception ex)
            {
                Log.Warn("Monitor de tema indisponível: " + ex.Message);
            }
        }
    }
}
