using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.App.Services;
using OtimizaPC.Core;
using OtimizaPC.Tweaks.Uninstall;

namespace OtimizaPC.App.ViewModels
{
    public sealed class InstalledProgramItemViewModel
    {
        public InstalledProgramItemViewModel(InstalledProgram program)
        {
            Program = program;
            Icon = LoadIcon(program.DisplayIcon);
        }

        public InstalledProgram Program { get; }
        public BitmapSource Icon { get; }
        public string InstallDateText => Format.Date(Program.InstallDate) ?? "—";
        public string EstimatedSizeText => Program.EstimatedSizeKb.HasValue
            ? Format.Bytes(Program.EstimatedSizeKb.Value * 1024d)
            : "—";

        private static BitmapSource LoadIcon(string displayIcon)
        {
            if (string.IsNullOrEmpty(displayIcon)) return null;

            var path = displayIcon;
            var separator = path.LastIndexOf(',');
            if (separator >= 0 &&
                int.TryParse(path.Substring(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                // O serviço extrai somente o ícone associado ao executável.
                if (index != 0) return null;
                path = path.Substring(0, separator);
            }

            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? IconService.FromExecutable(path) : null;
        }
    }

    public sealed partial class UninstallLeftoverItemViewModel : ObservableObject
    {
        public UninstallLeftoverItemViewModel(UninstallLeftover item)
        {
            Item = item;
            _isSelected = item.Confidence == LeftoverConfidence.High;
        }

        public UninstallLeftover Item { get; }

        [ObservableProperty]
        private bool _isSelected;
    }

    public partial class UninstallViewModel : ObservableObject, IActivatable
    {
        private readonly IDialogService _dialogs;
        private readonly LeftoverScanner _scanner = new LeftoverScanner();
        private readonly UninstallQuarantine _quarantine;
        private InstalledProgram _scannedProgram;
        private TaskCompletionSource<bool> _finishWaiting;
        private readonly InstalledProgramService _service = new InstalledProgramService();
        private IReadOnlyList<InstalledProgramItemViewModel> _programs = Array.Empty<InstalledProgramItemViewModel>();
        private bool _loaded;
        private bool _quarantineRefreshPending;

        [ObservableProperty]
        private string _searchText;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(ToggleSelectionCommand))]
        [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteQuarantineCommand))]
        private bool _isLoading;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(ToggleSelectionCommand))]
        [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteQuarantineCommand))]
        private bool _isUninstalling;

        [ObservableProperty]
        private string _uninstallProgress;

        [ObservableProperty]
        private bool _isWaitingForUninstallKey;

        [ObservableProperty]
        private IReadOnlyList<UninstallLeftoverItemViewModel> _leftovers;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(ToggleSelectionCommand))]
        [NotifyPropertyChangedFor(nameof(CleanupWarning))]
        private bool _cleanupBlocked;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(ToggleSelectionCommand))]
        [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteQuarantineCommand))]
        private bool _isManagingQuarantine;

        [ObservableProperty]
        private IReadOnlyList<UninstallQuarantineOperation> _quarantineOperations;

        [ObservableProperty]
        private string _quarantineSummary;

        [ObservableProperty]
        private IReadOnlyList<string> _quarantineSkipped;

        [ObservableProperty]
        private string _leftoverSummary;

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Summary))]
        private IReadOnlyList<InstalledProgramItemViewModel> _items = Array.Empty<InstalledProgramItemViewModel>();

        public UninstallViewModel(IDialogService dialogs)
        {
            _dialogs = dialogs;
            _quarantine = new UninstallQuarantine(_scanner);
        }

        public string Summary => Items.Count == _programs.Count
            ? $"{Items.Count} programa(s)"
            : $"{Items.Count} de {_programs.Count} programa(s)";

        public void OnActivated()
        {
            _quarantineRefreshPending = true;
            if (CanRefresh()) _ = LoadPageAsync();
        }

        public void OnDeactivated()
        {
        }

        partial void OnSearchTextChanged(string value) => ApplySearch();

        public string CleanupWarning => CleanupBlocked ? UninstallQuarantineResult.StillInstalledMessage : null;

        private bool CanRefresh() => !IsLoading && !IsUninstalling && !IsManagingQuarantine;

        private async Task LoadPageAsync()
        {
            if (!_quarantineRefreshPending || !CanRefresh()) return;

            _quarantineRefreshPending = false;
            if (!_loaded) await RefreshAsync();
            if (!CanRefresh()) return;

            await ManageQuarantineAsync(async () =>
            {
                QuarantineOperations = await Task.Run(() =>
                {
                    _quarantine.PurgeExpired();
                    return _quarantine.List();
                });
            });
        }

        private bool CanToggleSelection() => CanRefresh() && !CleanupBlocked && Leftovers?.Count > 0;

        private bool CanRemoveSelected() => CanToggleSelection() && Leftovers.Any(item => item.IsSelected);

        partial void OnLeftoversChanging(IReadOnlyList<UninstallLeftoverItemViewModel> value)
        {
            if (Leftovers == null) return;
            foreach (var item in Leftovers) item.PropertyChanged -= OnLeftoverSelectionChanged;
        }

        partial void OnLeftoversChanged(IReadOnlyList<UninstallLeftoverItemViewModel> value)
        {
            if (value != null)
                foreach (var item in value) item.PropertyChanged += OnLeftoverSelectionChanged;

            ToggleSelectionCommand.NotifyCanExecuteChanged();
            RemoveSelectedCommand.NotifyCanExecuteChanged();
        }

        private void OnLeftoverSelectionChanged(object sender, PropertyChangedEventArgs e) =>
            RemoveSelectedCommand.NotifyCanExecuteChanged();

        [RelayCommand(CanExecute = nameof(CanToggleSelection))]
        private void ToggleSelection()
        {
            var select = Leftovers.Any(item => !item.IsSelected);
            foreach (var item in Leftovers) item.IsSelected = select;
        }

        [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
        private Task RemoveSelectedAsync() => ManageQuarantineAsync(async () =>
        {
            var selected = Leftovers.Where(item => item.IsSelected).ToList();
            if (!await _dialogs.ConfirmAsync("Remover sobras",
                $"Mover {selected.Count} sobra(s) para a quarentena?", "Remover selecionadas")) return;

            var result = await Task.Run(() => _quarantine.Remove(_scannedProgram, selected.Select(item => item.Item).ToList()));
            CleanupBlocked = result.Blocked;
            ShowQuarantineResult(result, "removida(s)");
            await ReloadQuarantineAsync();
            if (!CleanupBlocked) await ScanLeftoversAsync(_scannedProgram);
        });

        private bool CanManageOperation(UninstallQuarantineOperation operation) => operation != null && CanRefresh();

        [RelayCommand(CanExecute = nameof(CanManageOperation))]
        private Task RestoreAsync(UninstallQuarantineOperation operation) => ManageQuarantineAsync(async () =>
        {
            var result = await Task.Run(() => _quarantine.Restore(operation));
            ShowQuarantineResult(result, "restaurada(s)");
            await ReloadQuarantineAsync();
        });

        [RelayCommand(CanExecute = nameof(CanManageOperation))]
        private Task DeleteQuarantineAsync(UninstallQuarantineOperation operation) => ManageQuarantineAsync(async () =>
        {
            if (!await _dialogs.ConfirmAsync("Excluir quarentena",
                $"Excluir definitivamente a quarentena de {operation.ProgramName} ({operation.DateText})?", "Excluir definitivamente")) return;

            await Task.Run(() => _quarantine.Delete(operation));
            QuarantineSummary = "Operação excluída definitivamente.";
            QuarantineSkipped = null;
            await ReloadQuarantineAsync();
        });

        private async Task ManageQuarantineAsync(Func<Task> action)
        {
            IsManagingQuarantine = true;
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Log.Error("Falha na quarentena do desinstalador", ex);
                QuarantineSummary = "Falha na quarentena. " + ex.Message;
                QuarantineSkipped = null;
            }
            finally
            {
                IsManagingQuarantine = false;
                if (_quarantineRefreshPending) _ = LoadPageAsync();
            }
        }

        private async Task ReloadQuarantineAsync() => QuarantineOperations = await Task.Run(() => _quarantine.List());

        private void ShowQuarantineResult(UninstallQuarantineResult result, string verb)
        {
            QuarantineSummary = $"{result.Completed} sobra(s) {verb}, {result.Skipped.Count} pulada(s).";
            QuarantineSkipped = result.Skipped;
        }

        private async Task ScanLeftoversAsync(InstalledProgram program)
        {
            var scan = await Task.Run(() =>
            {
                var leftovers = _scanner.Scan(program);
                return (Items: leftovers, Blocked: _scanner.UninstallKeyExists(program));
            });
            CleanupBlocked = scan.Blocked;
            _scannedProgram = program;
            LeftoverSummary = $"{program.DisplayName} — {scan.Items.Count} sobra(s) encontrada(s)";
            Leftovers = scan.Items.Select(item => new UninstallLeftoverItemViewModel(item)).ToList();
        }

        private bool CanUninstall(InstalledProgramItemViewModel item) => item != null && CanRefresh();

        [RelayCommand(CanExecute = nameof(CanUninstall))]
        private async Task UninstallAsync(InstalledProgramItemViewModel item)
        {
            IsUninstalling = true;
            var program = item.Program;
            try
            {
                if (!UninstallCommandParser.TryParse(program.UninstallString, out var executable, out var arguments))
                {
                    StatusMessage = "O comando de desinstalação do programa não é válido ou não é compatível.";
                    return;
                }

                if (!await _dialogs.ConfirmAsync("Desinstalar programa",
                    $"Abrir o desinstalador de {program.DisplayName}?", "Desinstalar")) return;

                StatusMessage = null;
                Leftovers = null;
                CleanupBlocked = false;
                QuarantineSummary = null;
                QuarantineSkipped = null;
                UninstallProgress = "Aguardando o desinstalador do programa…";
                var exitCode = await Task.Run(() =>
                {
                    using (var process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = true }))
                    {
                        if (process == null) return (int?)null;

                        process.WaitForExit();
                        return process.ExitCode;
                    }
                });

                await WaitForUninstallKeyAsync(program);

                UninstallProgress = "Procurando sobras…";
                await ScanLeftoversAsync(program);
                await RefreshAsync();

                if (exitCode.HasValue && exitCode != 0 && exitCode != 1641 && exitCode != 3010)
                {
                    var message = $"O desinstalador retornou o código {exitCode}. A remoção pode estar incompleta.";
                    StatusMessage = string.IsNullOrEmpty(StatusMessage) ? message : StatusMessage + " " + message;
                }
            }
            catch (Win32Exception ex)
            {
                StatusMessage = ex.NativeErrorCode == 1223
                    ? "A abertura do desinstalador foi cancelada."
                    : ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3
                        ? "Não foi possível encontrar o arquivo do desinstalador."
                        : "O Windows não conseguiu abrir o desinstalador.";
            }
            catch (Exception ex)
            {
                Log.Error("Falha na desinstalação ou na busca de sobras", ex);
                StatusMessage = "Não foi possível concluir a desinstalação ou a busca de sobras.";
            }
            finally
            {
                UninstallProgress = null;
                IsUninstalling = false;
                if (_quarantineRefreshPending) _ = LoadPageAsync();
            }
        }

        private async Task WaitForUninstallKeyAsync(InstalledProgram program)
        {
            if (!await Task.Run(() => _scanner.UninstallKeyExists(program))) return;

            _finishWaiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            UninstallProgress = "Aguardando a conclusão da desinstalação. Se já fechou o desinstalador, clique em Já terminei.";
            IsWaitingForUninstallKey = true;
            try
            {
                var elapsed = Stopwatch.StartNew();
                while (elapsed.Elapsed < TimeSpan.FromSeconds(60))
                {
                    var delay = Task.Delay(TimeSpan.FromSeconds(2));
                    if (await Task.WhenAny(delay, _finishWaiting.Task) == _finishWaiting.Task ||
                        !await Task.Run(() => _scanner.UninstallKeyExists(program)))
                        break;
                }
            }
            finally
            {
                IsWaitingForUninstallKey = false;
                _finishWaiting = null;
            }
        }

        [RelayCommand]
        private void FinishWaiting() => _finishWaiting?.TrySetResult(true);

        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private async Task RefreshAsync()
        {
            IsLoading = true;
            StatusMessage = null;
            try
            {
                _programs = await Task.Run(() => _service.ListInstalled()
                    .OrderBy(program => program.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(program => new InstalledProgramItemViewModel(program)).ToList());
                ApplySearch();
                _loaded = true;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao listar programas instalados", ex);
                StatusMessage = "Não foi possível listar os programas instalados.";
            }
            finally
            {
                IsLoading = false;
                if (_quarantineRefreshPending && CanRefresh()) _ = LoadPageAsync();
            }
        }

        private void ApplySearch()
        {
            var query = SearchText?.Trim();
            Items = string.IsNullOrEmpty(query) ? _programs : _programs.Where(item =>
                item.Program.DisplayName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                (item.Program.Publisher?.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) ?? -1) >= 0).ToList();
        }
    }
}
