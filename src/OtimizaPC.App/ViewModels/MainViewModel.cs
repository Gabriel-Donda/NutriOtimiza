using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.App.Services;
using OtimizaPC.Core;
using OtimizaPC.Core.Settings;
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
        public NavItem(string key, string title, SymbolRegular icon, object page)
        {
            Key = key;
            Title = title;
            Icon = icon;
            Page = page;
        }

        /// <summary>Chave estável usada pelos atalhos do painel (ex.: "sensors").</summary>
        public string Key { get; }

        public string Title { get; }

        public SymbolRegular Icon { get; }

        public object Page { get; }
    }

    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private NavItem _selectedItem;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasUpdateBanner))]
        private string _updateBanner;

        public MainViewModel(SensorService sensors, HardwareInfoService hardwareInfo, HealthService health, IDialogService dialogs, SettingsStore settingsStore)
        {
            var sensorSetup = new SensorSetupViewModel(sensors);
            Settings = new SettingsViewModel(settingsStore, dialogs, sensorSetup);
            Settings.UpdateFound += (_, __) =>
            {
                var version = Settings.AvailableUpdate.Version.ToString(3);
                UpdateBanner = Settings.IsUpdateReady
                    ? $"A versão {version} foi baixada e será instalada quando você fechar o {AppInfo.Name}."
                    : $"A versão {version} do {AppInfo.Name} está disponível.";
            };

            NavItems = new ObservableCollection<NavItem>
            {
                new NavItem("dashboard", "Painel", SymbolRegular.Home24, new DashboardViewModel(sensors, hardwareInfo, health, sensorSetup, NavigateTo)),
                new NavItem("hardware", "Hardware", SymbolRegular.DeveloperBoard24, new HardwareViewModel(hardwareInfo, settingsStore)),
                new NavItem("sensors", "Sensores", SymbolRegular.Temperature24, new SensorsViewModel(sensors, settingsStore)),
                new NavItem("cleanup", "Limpeza", SymbolRegular.Broom24, new CleanupViewModel(dialogs, settingsStore)),
                new NavItem("disk", "Espaço em disco", SymbolRegular.DataPie24, new DiskAnalyzerViewModel()),
                new NavItem("optimizations", "Otimizações", SymbolRegular.Flash24, new OptimizationsViewModel(dialogs)),
                new NavItem("startup", "Inicialização", SymbolRegular.Rocket24, new StartupViewModel()),
                new NavItem("apps", "Apps pré-instalados", SymbolRegular.Apps24, new AppsViewModel(dialogs)),
                new NavItem("repair", "Reparo", SymbolRegular.WrenchScrewdriver24, new RepairViewModel(dialogs)),
                new NavItem("settings", "Configurações", SymbolRegular.Settings24, Settings),
            };
            SelectedItem = NavItems[0];
        }

        public ObservableCollection<NavItem> NavItems { get; }

        /// <summary>Seleciona a página pela chave; chaves desconhecidas são ignoradas.</summary>
        public void NavigateTo(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            var item = NavItems.FirstOrDefault(n => string.Equals(n.Key, key, StringComparison.OrdinalIgnoreCase));
            if (item != null) SelectedItem = item;
        }

        public SettingsViewModel Settings { get; }

        public bool HasUpdateBanner => !string.IsNullOrEmpty(UpdateBanner);

        public Task CheckForUpdatesOnStartupAsync() => Settings.CheckOnStartupAsync();

        [RelayCommand]
        private void OpenSettings()
        {
            UpdateBanner = null;
            SelectedItem = NavItems.First(n => n.Page == Settings);
        }

        [RelayCommand]
        private void UpdateNow()
        {
            if (Settings.InstallUpdateCommand.CanExecute(null)) Settings.InstallUpdateCommand.Execute(null);
        }

        [RelayCommand]
        private void DismissUpdateBanner() => UpdateBanner = null;

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
