using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.App.Services;
using OtimizaPC.Core;
using OtimizaPC.Tweaks.Apps;

namespace OtimizaPC.App.ViewModels
{
    public partial class AppItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private string _statusText;

        public AppItemViewModel(AppPackage package, AppCatalogEntry entry)
        {
            Package = package;
            DisplayName = entry?.DisplayName ?? package.DisplayName;
            Description = entry?.Description ?? Format.Join(" · ", package.PublisherName, string.IsNullOrEmpty(package.Version) ? null : "versão " + package.Version);
        }

        public AppPackage Package { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public string PackageName => Package.Name;

        public BitmapSource Icon => IconService.FromImageFile(Package.LogoPath);
    }

    public sealed class AppGroupViewModel
    {
        public AppGroupViewModel(string name, string hint, IEnumerable<AppItemViewModel> items)
        {
            Name = name;
            Hint = hint;
            Items = new ObservableCollection<AppItemViewModel>(items);
        }

        public string Name { get; }

        public string Hint { get; }

        public ObservableCollection<AppItemViewModel> Items { get; }
    }

    public sealed class RemovedAppViewModel
    {
        public RemovedAppViewModel(RemovedApp app, IAsyncRelayCommand<RemovedAppViewModel> reinstall)
        {
            App = app;
            ReinstallCommand = reinstall;
        }

        public RemovedApp App { get; }

        public string DisplayName => App.DisplayName;

        public string Detail => $"{App.Name} · removido em {App.RemovedAt:dd/MM/yyyy HH:mm}";

        public BitmapSource Icon => IconService.FromImageFile(App.LogoPath);

        public IAsyncRelayCommand<RemovedAppViewModel> ReinstallCommand { get; }
    }

    public partial class AppsViewModel : ObservableObject, IActivatable
    {
        private readonly IDialogService _dialogs;
        private readonly AppxService _service = new AppxService();
        private bool _loaded;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(RefreshCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        private bool _alsoForNewUsers;

        public AppsViewModel(IDialogService dialogs)
        {
            _dialogs = dialogs;
            ReinstallCommand = new AsyncRelayCommand<RemovedAppViewModel>(ReinstallAsync);
        }

        public ObservableCollection<AppGroupViewModel> Groups { get; } = new ObservableCollection<AppGroupViewModel>();

        public ObservableCollection<RemovedAppViewModel> Removed { get; } = new ObservableCollection<RemovedAppViewModel>();

        public IAsyncRelayCommand<RemovedAppViewModel> ReinstallCommand { get; }

        public bool HasRemoved => Removed.Count > 0;

        public bool CanRemoveForNewUsers => AppInfo.IsAdministrator;

        private IEnumerable<AppItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

        public int SelectedCount => AllItems.Count(i => i.IsSelected);

        public string RemoveText => SelectedCount == 0 ? "Remover" : $"Remover {SelectedCount} app(s)";

        public void OnActivated()
        {
            if (_loaded) return;
            _loaded = true;
            _ = RefreshAsync();
        }

        public void OnDeactivated()
        {
        }

        private bool CanRefresh() => !IsBusy;

        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private async Task RefreshAsync()
        {
            IsBusy = true;
            StatusMessage = "Listando apps instalados…";
            try
            {
                var packages = await Task.Run(() =>
                {
                    var list = _service.ListInstalled();
                    _service.SyncRemoved(list);
                    return list;
                });
                BuildGroups(packages);
                LoadRemoved();
                StatusMessage = $"{AllItems.Count()} apps podem ser removidos. Apps essenciais (Store, codecs, segurança, drivers) ficam protegidos e não aparecem aqui.";
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao listar apps", ex);
                StatusMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void BuildGroups(IEnumerable<AppPackage> packages)
        {
            foreach (var item in AllItems) item.PropertyChanged -= OnItemChanged;
            Groups.Clear();

            var items = packages
                .Where(p => !AppCatalog.IsProtected(p))
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(p => (Package: p, Entry: AppCatalog.Find(p.Name)))
                .Select(x => (x.Entry, Item: new AppItemViewModel(x.Package, x.Entry)))
                .ToList();
            foreach (var (_, item) in items) item.PropertyChanged += OnItemChanged;

            AddGroup("Recomendado remover", "Propaganda, jogos pré-instalados e apps descontinuados.",
                items.Where(x => x.Entry?.Recommendation == AppRecommendation.Remove).Select(x => x.Item));
            AddGroup("Opcional", "Úteis para algumas pessoas. Remova o que você não usa.",
                items.Where(x => x.Entry?.Recommendation == AppRecommendation.Optional).Select(x => x.Item));
            AddGroup("Outros apps da Store", "Instalados por você ou pelo fabricante do PC. Confira antes de remover.",
                items.Where(x => x.Entry == null).Select(x => x.Item));
            OnItemChanged(null, new PropertyChangedEventArgs(nameof(AppItemViewModel.IsSelected)));
        }

        private void AddGroup(string name, string hint, IEnumerable<AppItemViewModel> items)
        {
            var list = items.OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (list.Count > 0) Groups.Add(new AppGroupViewModel(name, hint, list));
        }

        private void LoadRemoved()
        {
            Removed.Clear();
            foreach (var app in _service.LoadRemoved().OrderByDescending(a => a.RemovedAt))
                Removed.Add(new RemovedAppViewModel(app, ReinstallCommand));
            OnPropertyChanged(nameof(HasRemoved));
        }

        private void OnItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AppItemViewModel.IsSelected)) return;
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(RemoveText));
            RemoveCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SelectRecommended()
        {
            foreach (var group in Groups)
                foreach (var item in group.Items)
                    item.IsSelected = AppCatalog.Find(item.PackageName)?.Recommendation == AppRecommendation.Remove;
        }

        [RelayCommand]
        private void SelectNone()
        {
            foreach (var item in AllItems) item.IsSelected = false;
        }

        private bool CanRemove() => !IsBusy && SelectedCount > 0;

        [RelayCommand(CanExecute = nameof(CanRemove))]
        private async Task RemoveAsync()
        {
            var selected = AllItems.Where(i => i.IsSelected).ToList();
            var forNewUsers = AlsoForNewUsers && CanRemoveForNewUsers;
            if (!await _dialogs.ConfirmAsync("Remover apps", BuildConfirmation(selected, forNewUsers), "Remover"))
                return;

            IsBusy = true;
            var removed = 0;
            var failed = 0;
            try
            {
                foreach (var item in selected)
                {
                    StatusMessage = "Removendo: " + item.DisplayName;
                    item.StatusText = "Removendo…";
                    try
                    {
                        await Task.Run(() => _service.Remove(item.Package, item.DisplayName, forNewUsers));
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Log.Error($"Falha ao remover {item.PackageName}", ex);
                        item.StatusText = "Erro: " + ex.Message;
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }

            await RefreshAsync();
            StatusMessage = $"{removed} app(s) removido(s)" + (failed > 0 ? $", {failed} com erro (veja o log)." : ". Para trazer algum de volta, use \"Reinstalar\".");
        }

        private static string BuildConfirmation(IReadOnlyList<AppItemViewModel> selected, bool forNewUsers)
        {
            var sb = new StringBuilder();
            sb.Append($"Remover {selected.Count} app(s): ");
            sb.Append(string.Join(", ", selected.Take(12).Select(i => i.DisplayName)));
            if (selected.Count > 12) sb.Append($" e mais {selected.Count - 12}");
            sb.Append('.');
            if (forNewUsers) sb.Append("\n\nTambém serão removidos do pacote de instalação para novos usuários deste PC.");
            sb.Append("\n\nPara trazer um app de volta, use \"Reinstalar\" na lista de removidos (ou a Microsoft Store).");
            return sb.ToString();
        }

        private async Task ReinstallAsync(RemovedAppViewModel item)
        {
            if (item == null) return;
            StatusMessage = "Reinstalando: " + item.DisplayName;
            try
            {
                var outcome = await Task.Run(() => _service.Reinstall(item.App));
                StatusMessage = outcome == ReinstallOutcome.Reinstalled
                    ? $"{item.DisplayName} foi reinstalado."
                    : $"Os arquivos de {item.DisplayName} não estão mais no PC: a página dele na Microsoft Store foi aberta.";
                if (outcome == ReinstallOutcome.Reinstalled) await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao reinstalar {item.App.Name}", ex);
                StatusMessage = "Não foi possível reinstalar: " + ex.Message;
            }
        }
    }
}
