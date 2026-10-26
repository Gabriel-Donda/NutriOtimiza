using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.App.Services;
using OtimizaPC.Core;
using OtimizaPC.Tweaks.Startup;

namespace OtimizaPC.App.ViewModels
{
    public partial class StartupItemViewModel : ObservableObject
    {
        private readonly Action<string> _report;
        private bool _suppress;

        [ObservableProperty]
        private bool _isEnabled;

        public StartupItemViewModel(StartupEntry entry, Action<string> report)
        {
            Entry = entry;
            _report = report;
            _suppress = true;
            IsEnabled = entry.IsEnabled;
            _suppress = false;
        }

        public StartupEntry Entry { get; }

        public string Name => Entry.Name;

        public string Publisher => Entry.Publisher ?? "Fornecedor desconhecido";

        public string Command => Entry.Command;

        public string Location => Entry.LocationText;

        public bool CanToggle => !Entry.RequiresAdmin || AppInfo.IsAdministrator;

        public BitmapSource Icon => IconService.FromExecutable(Entry.ExecutablePath);

        public string ActionText => IsEnabled ? "Desativar" : "Ativar";

        partial void OnIsEnabledChanged(bool value)
        {
            OnPropertyChanged(nameof(ActionText));
            if (_suppress) return;
            try
            {
                StartupManager.SetEnabled(Entry, value);
                _report(null);
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao alterar a inicialização de '{Entry.Name}'", ex);
                _suppress = true;
                IsEnabled = !value;
                _suppress = false;
                _report($"Não foi possível alterar \"{Entry.Name}\": {ex.Message}");
            }
        }

        [RelayCommand]
        private void ToggleEnabled() { if (CanToggle) IsEnabled = !IsEnabled; }

        [RelayCommand]
        private void OpenLocation()
        {
            var path = Entry.ExecutablePath;
            try
            {
                if (path != null && File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
                else _report("Arquivo não encontrado: " + (path ?? Entry.Command));
            }
            catch (Exception ex)
            {
                _report("Não foi possível abrir o local: " + ex.Message);
            }
        }
    }

    public partial class StartupViewModel : ObservableObject, IActivatable
    {
        private bool _loaded;

        [ObservableProperty]
        private string _summary;

        [ObservableProperty]
        private string _statusMessage;

        public ObservableCollection<StartupItemViewModel> Items { get; } = new ObservableCollection<StartupItemViewModel>();

        public void OnActivated()
        {
            if (_loaded) return;
            _loaded = true;
            _ = RefreshAsync();
        }

        public void OnDeactivated()
        {
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            var entries = await Task.Run(() => StartupManager.List());
            foreach (var item in Items) item.PropertyChanged -= OnItemChanged;
            Items.Clear();
            foreach (var entry in entries)
            {
                var item = new StartupItemViewModel(entry, message => StatusMessage = message);
                item.PropertyChanged += OnItemChanged;
                Items.Add(item);
            }
            UpdateSummary();
            StatusMessage = AppInfo.IsAdministrator ? null : "Sem administrador: itens de \"todos os usuários\" não podem ser alterados.";
        }

        private void OnItemChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StartupItemViewModel.IsEnabled)) UpdateSummary();
        }

        private void UpdateSummary()
        {
            var active = Items.Count(i => i.IsEnabled);
            Summary = $"{active} de {Items.Count} programas abrem junto com o Windows. Desativar não desinstala nada: o programa só deixa de abrir sozinho.";
        }
    }
}
