using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.App.Services;
using OtimizaPC.Core;
using OtimizaPC.Tweaks;
using OtimizaPC.Tweaks.SystemTools;

namespace OtimizaPC.App.ViewModels
{
    public partial class TweakItemViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPending))]
        private bool _isOn;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsActive))]
        private TweakState _state = TweakState.Unknown;

        [ObservableProperty]
        private string _statusText;

        [ObservableProperty]
        private bool _hasError;

        public TweakItemViewModel(Tweak tweak)
        {
            Tweak = tweak;
            IsEnabled = !tweak.RequiresAdmin || AppInfo.IsAdministrator;
        }

        public Tweak Tweak { get; }

        public string Name => Tweak.Name;

        public string Description => Tweak.Description;

        public bool IsEnabled { get; }

        public bool IsActive => State == TweakState.Applied;

        public bool IsPending => IsEnabled && State != TweakState.Unknown && IsOn != IsActive;

        public bool IsModerate => Tweak.Risk == TweakRisk.Moderate;

        public bool IsAdvanced => Tweak.Risk == TweakRisk.Advanced;

        public string RestartText
        {
            get
            {
                switch (Tweak.Restart)
                {
                    case RestartRequirement.Explorer: return "Reinicia o Explorador";
                    case RestartRequirement.Logoff: return "Requer sair e entrar";
                    case RestartRequirement.Reboot: return "Requer reiniciar";
                    default: return null;
                }
            }
        }

        public void ApplyState(TweakState state, DateTime? appliedByAppAt)
        {
            State = state;
            IsOn = state == TweakState.Applied;
            HasError = false;

            if (!IsEnabled) StatusText = "Requer administrador";
            else if (state == TweakState.Unknown) StatusText = "Não disponível neste computador";
            else if (state == TweakState.Partial) StatusText = "Parcialmente aplicado";
            else if (state == TweakState.Applied)
                StatusText = appliedByAppAt.HasValue ? $"Aplicado pelo {AppInfo.Name} em {appliedByAppAt:dd/MM/yyyy}" : "Já está assim no sistema";
            else StatusText = null;
        }

        public void ApplyError(string message)
        {
            HasError = true;
            StatusText = message;
        }
    }

    public sealed class TweakGroupViewModel
    {
        public TweakGroupViewModel(string name, IEnumerable<TweakItemViewModel> items)
        {
            Name = name;
            Items = new ObservableCollection<TweakItemViewModel>(items);
        }

        public string Name { get; }

        public ObservableCollection<TweakItemViewModel> Items { get; }
    }

    public partial class OptimizationsViewModel : ObservableObject, IActivatable
    {
        private readonly IDialogService _dialogs;
        private readonly TweakService _service = new TweakService();
        private bool _loaded;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DiscardCommand), nameof(SelectRecommendedCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private bool _createRestorePoint = true;

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        private bool _needsExplorerRestart;

        public OptimizationsViewModel(IDialogService dialogs)
        {
            _dialogs = dialogs;
        }

        public ObservableCollection<TweakGroupViewModel> Groups { get; } = new ObservableCollection<TweakGroupViewModel>();

        private IEnumerable<TweakItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

        public int PendingCount => AllItems.Count(i => i.IsPending);

        public string ApplyText => PendingCount == 0 ? "Aplicar" : $"Aplicar {PendingCount} alteração(ões)";

        public bool CanCreateRestorePoint => AppInfo.IsAdministrator && !OsInfo.IsServer;

        public async void OnActivated()
        {
            if (_loaded) return;
            _loaded = true;

            foreach (var group in TweakCatalog.Create()
                         .GroupBy(t => t.Category)
                         .OrderBy(g => Array.IndexOf(TweakCategories.Order, g.Key)))
            {
                var items = group.Select(t => new TweakItemViewModel(t)).ToList();
                foreach (var item in items) item.PropertyChanged += OnItemChanged;
                Groups.Add(new TweakGroupViewModel(group.Key, items));
            }

            if (!AppInfo.IsAdministrator)
                StatusMessage = "Executando sem administrador: só os ajustes do seu usuário estão disponíveis.";
            await RefreshStatesAsync();
        }

        public void OnDeactivated()
        {
        }

        private void OnItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TweakItemViewModel.IsPending)) return;
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(ApplyText));
            ApplyCommand.NotifyCanExecuteChanged();
            DiscardCommand.NotifyCanExecuteChanged();
        }

        private async Task RefreshStatesAsync()
        {
            var items = AllItems.ToList();
            // Ler o estado pode chamar powercfg/serviços: fora da thread da interface
            var states = await Task.Run(() => items.Select(i => (i, _service.GetState(i.Tweak), _service.AppliedByAppAt(i.Tweak))).ToList());
            foreach (var (item, state, appliedAt) in states)
                item.ApplyState(state, appliedAt);
        }

        private bool CanApply() => !IsBusy && PendingCount > 0;

        [RelayCommand(CanExecute = nameof(CanApply))]
        private async Task ApplyAsync()
        {
            var pending = AllItems.Where(i => i.IsPending).ToList();
            var toApply = pending.Where(i => i.IsOn).ToList();
            var toRevert = pending.Where(i => !i.IsOn).ToList();

            if (!await _dialogs.ConfirmAsync("Confirmar alterações", BuildConfirmation(toApply, toRevert), "Aplicar"))
                return;

            IsBusy = true;
            try
            {
                if (CreateRestorePoint && CanCreateRestorePoint)
                {
                    StatusMessage = "Criando ponto de restauração (pode levar um minuto)…";
                    var restore = await Task.Run(() => RestorePoint.TryCreate($"{AppInfo.Name} - antes das otimizações"));
                    if (!restore.Success
                        && !await _dialogs.ConfirmAsync("Sem ponto de restauração", restore.Message + $"\n\nContinuar mesmo assim? Cada otimização ainda pode ser desfeita pelo {AppInfo.Name}.", "Continuar"))
                    {
                        StatusMessage = "Nenhuma alteração foi feita.";
                        return;
                    }
                    StatusMessage = restore.Message;
                }

                var failures = 0;
                foreach (var item in pending)
                {
                    StatusMessage = (item.IsOn ? "Aplicando: " : "Revertendo: ") + item.Name;
                    var apply = item.IsOn;
                    try
                    {
                        await Task.Run(() =>
                        {
                            if (apply) _service.Apply(item.Tweak);
                            else _service.Revert(item.Tweak);
                        });
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Log.Error($"Falha ao {(apply ? "aplicar" : "reverter")} '{item.Tweak.Id}'", ex);
                        item.ApplyError("Erro: " + ex.Message);
                    }
                }

                var errors = AllItems.Where(i => i.HasError).ToDictionary(i => i, i => i.StatusText);
                await RefreshStatesAsync();
                foreach (var error in errors) error.Key.ApplyError(error.Value);

                var changed = pending.Where(i => !i.HasError).ToList();
                NeedsExplorerRestart = changed.Any(i => i.Tweak.Restart == RestartRequirement.Explorer);
                StatusMessage = BuildResult(changed.Count, failures, changed);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string BuildConfirmation(IReadOnlyList<TweakItemViewModel> toApply, IReadOnlyList<TweakItemViewModel> toRevert)
        {
            var sb = new StringBuilder();
            if (toApply.Count > 0) sb.Append($"Aplicar {toApply.Count} otimização(ões).");
            if (toRevert.Count > 0) sb.Append(sb.Length > 0 ? " " : string.Empty).Append($"Reverter {toRevert.Count}.");

            var risky = toApply.Where(i => i.Tweak.Risk != TweakRisk.Safe).Select(i => i.Name).ToList();
            if (risky.Count > 0)
                sb.Append("\n\nCom efeito colateral: ").Append(string.Join("; ", risky)).Append('.');

            sb.Append("\n\nTudo pode ser desfeito depois, desligando a opção e aplicando de novo.");
            return sb.ToString();
        }

        private static string BuildResult(int changed, int failures, IReadOnlyList<TweakItemViewModel> items)
        {
            var text = $"{changed} alteração(ões) concluída(s)";
            if (failures > 0) text += $", {failures} com erro";
            if (items.Any(i => i.Tweak.Restart == RestartRequirement.Reboot)) text += ". Reinicie o computador para concluir.";
            else if (items.Any(i => i.Tweak.Restart == RestartRequirement.Logoff)) text += ". Saia e entre na sua conta para concluir.";
            else text += ".";
            return text;
        }

        private bool CanEdit() => !IsBusy && PendingCount > 0;

        [RelayCommand(CanExecute = nameof(CanEdit))]
        private void Discard()
        {
            foreach (var item in AllItems) item.IsOn = item.IsActive;
        }

        private bool CanSelect() => !IsBusy;

        [RelayCommand(CanExecute = nameof(CanSelect))]
        private void SelectRecommended()
        {
            foreach (var item in AllItems.Where(i => i.IsEnabled && i.Tweak.Recommended && i.State != TweakState.Unknown))
                item.IsOn = true;
        }

        [RelayCommand]
        private async Task RestartExplorerAsync()
        {
            NeedsExplorerRestart = false;
            await Task.Run(() =>
            {
                foreach (var process in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Não foi possível encerrar o Explorador: " + ex.Message);
                    }
                    process.Dispose();
                }
            });

            // O Windows normalmente reabre o Explorador sozinho; se não reabrir, abrimos
            await Task.Delay(2000);
            if (Process.GetProcessesByName("explorer").Length == 0)
                Process.Start("explorer.exe");
            StatusMessage = "Explorador reiniciado.";
        }
    }
}
