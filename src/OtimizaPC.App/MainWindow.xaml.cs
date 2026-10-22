using System;
using OtimizaPC.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace OtimizaPC.App
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += (_, __) =>
            {
                try
                {
                    // Acompanha a troca de tema claro/escuro do Windows 10/11
                    SystemThemeWatcher.Watch(this);
                }
                catch (Exception ex)
                {
                    Log.Warn("Monitor de tema indisponível: " + ex.Message);
                }
            };
        }
    }
}
