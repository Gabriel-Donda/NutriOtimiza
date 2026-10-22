using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.App.Services;
using OtimizaPC.Cleaning;
using OtimizaPC.Core;

namespace OtimizaPC.App.ViewModels
{
    public enum CleanupItemState
    {
        Idle,
        Scanning,
        Scanned,
        Cleaning,
        Cleaned,
        Error,
    }

    public partial class CleanupItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private CleanupItemState _state = CleanupItemState.Idle;

        [ObservableProperty]
        private string _sizeText = "—";

        [ObservableProperty]
        private string _statusText;

        public CleanupItemViewModel(Cleaner cleaner)
        {
            Cleaner = cleaner;
            IsEnabled = !cleaner.RequiresAdmin || AppInfo.IsAdministrator;
            IsSelected = cleaner.DefaultSelected && IsEnabled;
            StatusText = IsEnabled ? null : "Requer administrador";
        }

        public Cleaner Cleaner { get; }

        public string Name => Cleaner.Name;

        public string Description => Cleaner.Description;

        public bool IsEnabled { get; }

        public bool IsModerate => Cleaner.Risk == CleanupRisk.Moderate;

        public CleanupScan Scan { get; private set; }

        /// <summary>Aplicativo aberto no momento da análise (ex.: navegador); não entra no total a liberar.</summary>
        [ObservableProperty]
        private bool _isBlocked;

        public long SelectedBytes => IsSelected && !IsBlocked && Scan != null && State == CleanupItemState.Scanned ? Scan.Bytes : 0;

        public void ApplyScan(CleanupScan scan)
        {
            Scan = scan;
            State = CleanupItemState.Scanned;
            if (!scan.SizeKnown)
            {
                SizeText = "?";
                StatusText = scan.Note;
                return;
            }

            SizeText = scan.Bytes > 0 ? Format.Bytes(scan.Bytes) : "Vazio";
            var parts = new List<string>();
            if (scan.ItemCount > 0) parts.Add($"{scan.ItemCount:N0} item(ns)");
            if (scan.InaccessibleFolders > 0) parts.Add($"{scan.InaccessibleFolders} pasta(s) sem acesso");
            if (!string.IsNullOrEmpty(scan.Note)) parts.Add(scan.Note);
            var blocked = Cleaner.GetBlockedReason();
            IsBlocked = blocked != null;
            if (blocked != null) parts.Add(blocked);
            StatusText = parts.Count > 0 ? string.Join(" · ", parts) : null;
            OnPropertyChanged(nameof(SelectedBytes));
        }

        public void ApplyResult(CleanResult result)
        {
            State = CleanupItemState.Cleaned;
            SizeText = Format.Bytes(result.FreedBytes);
            var text = "Liberado";
            if (result.Skipped > 0) text += $" · {result.Skipped:N0} em uso foram mantidos";
            if (!string.IsNullOrEmpty(result.Note)) text += " · " + result.Note;
            StatusText = text;
            OnPropertyChanged(nameof(SelectedBytes));
        }

        public void ApplyError(string message)
        {
            State = CleanupItemState.Error;
            StatusText = message;
            OnPropertyChanged(nameof(SelectedBytes));
        }

        public void ApplySkipped(string reason)
        {
            IsBlocked = true;
            StatusText = reason;
            OnPropertyChanged(nameof(SelectedBytes));
        }

        partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(SelectedBytes));

        partial void OnIsBlockedChanged(bool value) => OnPropertyChanged(nameof(SelectedBytes));
    }

    public sealed class CleanupGroupViewModel
    {
        public CleanupGroupViewModel(string name, IEnumerable<CleanupItemViewModel> items)
        {
            Name = name;
            Items = new ObservableCollection<CleanupItemViewModel>(items);
        }

        public string Name { get; }

        public ObservableCollection<CleanupItemViewModel> Items { get; }
    }

    public partial class CleanupViewModel : ObservableObject, IActivatable
    {
        private const int ScanConcurrency = 4;

        private readonly IDialogService _dialogs;
        private CancellationTokenSource _cts;
        private bool _loaded;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand), nameof(CancelCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private bool _hasScanned;

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private string _headline = "Analise o computador para ver quanto espaço pode ser liberado";

        [ObservableProperty]
        private string _statusMessage;

        public CleanupViewModel(IDialogService dialogs)
        {
            _dialogs = dialogs;
        }

        public ObservableCollection<CleanupGroupViewModel> Groups { get; } = new ObservableCollection<CleanupGroupViewModel>();

        private IEnumerable<CleanupItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

        public string SelectedTotalText => Format.Bytes(AllItems.Sum(i => i.SelectedBytes));

        public bool CanClean => HasScanned && !IsBusy;

        public void OnActivated()
        {
            if (_loaded) return;
            _loaded = true;
            LoadCatalog();
        }

        public void OnDeactivated()
        {
        }

        private void LoadCatalog()
        {
            var order = new[] { CleanupGroups.Windows, CleanupGroups.Browsers, CleanupGroups.Apps };
            foreach (var group in CleanupCatalog.Create().GroupBy(c => c.Group).OrderBy(g => Array.IndexOf(order, g.Key)))
            {
                var items = group.Select(c => new CleanupItemViewModel(c)).ToList();
                foreach (var item in items) item.PropertyChanged += OnItemChanged;
                Groups.Add(new CleanupGroupViewModel(group.Key, items));
            }
            if (!AppInfo.IsAdministrator)
                StatusMessage = "Executando sem administrador: itens do sistema estão desabilitados.";
        }

        private void OnItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CleanupItemViewModel.SelectedBytes))
                OnPropertyChanged(nameof(SelectedTotalText));
        }

        partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanClean));

        partial void OnHasScannedChanged(bool value)
        {
            OnPropertyChanged(nameof(CanClean));
            CleanCommand.NotifyCanExecuteChanged();
        }

        private bool CanScan() => !IsBusy;

        [RelayCommand(CanExecute = nameof(CanScan))]
        private async Task ScanAsync()
        {
            var items = AllItems.Where(i => i.IsEnabled).ToList();
            if (items.Count == 0) return;

            IsBusy = true;
            Progress = 0;
            Headline = "Analisando…";
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var done = 0;

            try
            {
                using (var gate = new SemaphoreSlim(ScanConcurrency))
                {
                    var tasks = items.Select(async item =>
                    {
                        await gate.WaitAsync(token);
                        try
                        {
                            item.State = CleanupItemState.Scanning;
                            item.StatusText = "Analisando…";
                            var scan = await Task.Run(() => item.Cleaner.Scan(token), token);
                            item.ApplyScan(scan);
                        }
                        catch (OperationCanceledException)
                        {
                            item.State = CleanupItemState.Idle;
                            item.StatusText = "Cancelado";
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Falha ao analisar '{item.Name}'", ex);
                            item.ApplyError("Erro: " + ex.Message);
                        }
                        finally
                        {
                            gate.Release();
                            Progress = ++done * 100.0 / items.Count;
                        }
                    });
                    await Task.WhenAll(tasks);
                }

                HasScanned = true;
                var total = items.Where(i => i.Scan != null).Sum(i => i.Scan.Bytes);
                Headline = token.IsCancellationRequested
                    ? "Análise cancelada"
                    : $"{Format.Bytes(total)} podem ser liberados";
                Log.Info($"Análise de limpeza concluída: {Format.Bytes(total)} em {items.Count} itens");
            }
            catch (OperationCanceledException)
            {
                Headline = "Análise cancelada";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(SelectedTotalText));
            }
        }

        private bool CanRunClean() => HasScanned && !IsBusy;

        [RelayCommand(CanExecute = nameof(CanRunClean))]
        private async Task CleanAsync()
        {
            var candidates = AllItems.Where(i => i.IsSelected && i.IsEnabled && i.State == CleanupItemState.Scanned).ToList();

            // Reavalia agora: o usuário pode ter fechado (ou aberto) um navegador depois da análise
            foreach (var item in candidates) item.IsBlocked = item.Cleaner.GetBlockedReason() != null;
            var selected = candidates.Where(i => !i.IsBlocked).ToList();
            var blockedNames = candidates.Where(i => i.IsBlocked).Select(i => i.Name).ToList();

            if (selected.Count == 0)
            {
                StatusMessage = blockedNames.Count > 0
                    ? "Feche os aplicativos indicados para limpar: " + string.Join(", ", blockedNames)
                    : "Nenhum item analisado está selecionado.";
                return;
            }

            if (!await _dialogs.ConfirmAsync("Confirmar limpeza", BuildConfirmation(selected, blockedNames), "Limpar"))
                return;

            IsBusy = true;
            Progress = 0;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            long freed = 0;
            var skipped = 0;

            try
            {
                for (var i = 0; i < selected.Count; i++)
                {
                    var item = selected[i];
                    if (token.IsCancellationRequested) break;

                    var blocked = item.Cleaner.GetBlockedReason();
                    if (blocked != null)
                    {
                        item.ApplySkipped(blocked);
                        continue;
                    }

                    Headline = $"Limpando: {item.Name}";
                    item.State = CleanupItemState.Cleaning;
                    item.StatusText = "Limpando…";
                    try
                    {
                        var result = await Task.Run(() => item.Cleaner.Clean(item.Scan, token), token);
                        item.ApplyResult(result);
                        freed += result.FreedBytes;
                        skipped += result.Skipped;
                        Log.Info($"Limpeza '{item.Cleaner.Id}': {Format.Bytes(result.FreedBytes)} liberados, {result.Deleted} removidos, {result.Skipped} mantidos");
                    }
                    catch (OperationCanceledException)
                    {
                        item.ApplyError("Cancelado (parte dos arquivos pode ter sido removida)");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Falha ao limpar '{item.Name}'", ex);
                        item.ApplyError("Erro: " + ex.Message);
                    }
                    Progress = (i + 1) * 100.0 / selected.Count;
                }

                Headline = $"{Format.Bytes(freed)} liberados";
                StatusMessage = skipped > 0
                    ? $"{skipped:N0} arquivo(s) em uso foram mantidos. Feche os programas e analise de novo para removê-los."
                    : "Limpeza concluída.";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(SelectedTotalText));
            }
        }

        private static string BuildConfirmation(IReadOnlyList<CleanupItemViewModel> selected, IReadOnlyList<string> blockedNames)
        {
            var known = selected.Where(i => i.Scan.SizeKnown).Sum(i => i.Scan.Bytes);
            var sb = new StringBuilder();
            sb.Append($"Serão limpos {selected.Count} item(ns), liberando cerca de {Format.Bytes(known)}.");

            if (blockedNames.Count > 0)
                sb.Append("\n\nIgnorados porque estão abertos: ").Append(string.Join(", ", blockedNames)).Append('.');

            var moderate = selected.Where(i => i.IsModerate).Select(i => i.Name).ToList();
            if (moderate.Count > 0)
                sb.Append("\n\nAtenção, itens com efeito colateral: ").Append(string.Join(", ", moderate)).Append('.');

            if (selected.Any(i => !i.Scan.SizeKnown))
                sb.Append("\n\nA limpeza de componentes do Windows (DISM) pode levar vários minutos.");

            sb.Append("\n\nOs arquivos removidos não vão para a lixeira.");
            return sb.ToString();
        }

        private bool CanCancel() => IsBusy;

        [RelayCommand(CanExecute = nameof(CanCancel))]
        private void Cancel()
        {
            _cts?.Cancel();
            StatusMessage = "Cancelando…";
        }

        [RelayCommand]
        private void SelectRecommended()
        {
            foreach (var item in AllItems)
                item.IsSelected = item.IsEnabled && item.Cleaner.DefaultSelected;
        }

        [RelayCommand]
        private void SelectNone()
        {
            foreach (var item in AllItems) item.IsSelected = false;
        }
    }
}
