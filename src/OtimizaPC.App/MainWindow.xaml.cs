using OtimizaPC.App.Services;
using OtimizaPC.Core.Settings;
using Wpf.Ui.Controls;

namespace OtimizaPC.App
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            // Acompanha a troca de tema claro/escuro do Windows 10/11 quando a preferência é "Sistema"
            Loaded += (_, __) => ThemeService.Watch(new SettingsStore().Load().Theme);
        }
    }
}
