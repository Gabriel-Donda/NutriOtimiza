using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OtimizaPC.App.Services;
using OtimizaPC.Core;
using OtimizaPC.Hardware;
using OtimizaPC.Hardware.Sensors;
using Wpf.Ui.Controls;

namespace OtimizaPC.App.ViewModels
{
    /// <summary>Página que precisa saber quando entra/sai da tela (para ligar/desligar timers).</summary>
    public interface IActivatable
    {
        void OnActivated();

        void OnDeactivated();
    }

    public sealed class NavItem
    {
        public NavItem(string title, SymbolRegular icon, object page)
        {
            Title = title;
            Icon = icon;
            Page = page;
        }

        public string Title { get; }

        public SymbolRegular Icon { get; }

        public object Page { get; }
    }

    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private NavItem _selectedItem;

        public MainViewModel(SensorService sensors, HardwareInfoService hardwareInfo, HealthService health, IDialogService dialogs)
        {
            NavItems = new ObservableCollection<NavItem>
            {
                new NavItem("Painel", SymbolRegular.Home24, new DashboardViewModel(sensors, hardwareInfo, health)),
                new NavItem("Hardware", SymbolRegular.DeveloperBoard24, new HardwareViewModel(hardwareInfo)),
                new NavItem("Sensores", SymbolRegular.Temperature24, new SensorsViewModel(sensors)),
                new NavItem("Limpeza", SymbolRegular.Broom24, new CleanupViewModel(dialogs)),
                new NavItem("Espaço em disco", SymbolRegular.DataPie24, new DiskAnalyzerViewModel()),
                new NavItem("Otimizações", SymbolRegular.Flash24, new OptimizationsViewModel(dialogs)),
                new NavItem("Inicialização", SymbolRegular.Rocket24, new StartupViewModel()),
                new NavItem("Apps pré-instalados", SymbolRegular.Apps24, new AppsViewModel(dialogs)),
                new NavItem("Reparo", SymbolRegular.WrenchScrewdriver24, new RepairViewModel(dialogs)),
                new NavItem("Configurações", SymbolRegular.Settings24, new ComingSoonViewModel(
                    "Configurações", SymbolRegular.Settings24, 5,
                    "Preferências do aplicativo.",
                    "Agendamento de limpeza automática",
                    "Histórico de alterações com desfazer",
                    "Tema claro/escuro e alertas de temperatura",
                    "Atualização automática")),
            };
            SelectedItem = NavItems[0];
        }

        public ObservableCollection<NavItem> NavItems { get; }

        public string VersionText => $"{AppInfo.Name} v{AppInfo.Version}";

        public bool IsAdministrator => AppInfo.IsAdministrator;

        public string PrivilegeText => IsAdministrator ? "Administrador" : "Sem privilégios de administrador";

        partial void OnSelectedItemChanged(NavItem oldValue, NavItem newValue)
        {
            (oldValue?.Page as IActivatable)?.OnDeactivated();
            (newValue?.Page as IActivatable)?.OnActivated();
        }
    }
}
