using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.App.Services;
using OtimizaPC.Core;
using OtimizaPC.Core.Models;
using OtimizaPC.Repair.Console;
using OtimizaPC.Repair.Diagnostics;
using OtimizaPC.Repair.Fixes;
using OtimizaPC.Repair.Tools;

namespace OtimizaPC.App.ViewModels
{
    public sealed class RepairToolItemViewModel
    {
        /// <summary>
        /// Cada item tem o próprio comando: com um comando compartilhado + CommandParameter, o WPF do .NET Framework
        /// avalia CanExecute antes do parâmetro chegar (null) e o botão fica desabilitado para sempre.
        /// </summary>
        public RepairToolItemViewModel(RepairTool tool, Func<RepairToolItemViewModel, Task> run, Func<bool> isBusy)
        {
            Tool = tool;
            IsEnabled = !tool.RequiresAdmin || AppInfo.IsAdministrator;
            RunCommand = new AsyncRelayCommand(() => run(this), () => IsEnabled && !isBusy());
        }

        public RepairTool Tool { get; }

        public string Name => Tool.Name;

        public string Description => Tool.Description;

        public string Detail => Format.Join(" · ", "Duração: " + Tool.Duration, Tool.RequiresRestart ? "requer reiniciar" : null, IsEnabled ? null : "requer administrador");

        public bool IsEnabled { get; }

        public IAsyncRelayCommand RunCommand { get; }
    }

    public sealed class RepairToolGroupViewModel
    {
        public RepairToolGroupViewModel(string name, IEnumerable<RepairToolItemViewModel> items)
        {
            Name = name;
            Items = items.ToList();
        }

        public string Name { get; }

        public IReadOnlyList<RepairToolItemViewModel> Items { get; }
    }

    public sealed class ConsoleLineViewModel
    {
        public ConsoleLineViewModel(string text, bool isError, bool isProgress)
        {
            Text = text;
            IsError = isError;
            IsProgress = isProgress;
        }

        public string Text { get; }

        public bool IsError { get; }

        public bool IsProgress { get; }
    }

    /// <summary>Resultado da execução de um EventFix exibido no cartão.</summary>
    public sealed class EventFixResultViewModel
    {
        public bool Success { get; set; }
        public string Summary { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>ViewModel de um EventFix individual, com seu próprio comando e progresso.</summary>
    public sealed partial class EventFixItemViewModel : ObservableObject
    {
        private readonly EventFix _fix;
        private readonly Func<EventFixItemViewModel, Task> _run;
        private readonly Func<bool> _isAnyRunning;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanRun))]
        private bool _isRunning;

        [ObservableProperty]
        private int _progress;

        [ObservableProperty]
        private string _progressText = "0%";

        [ObservableProperty]
        private EventFixResultViewModel _result;

        public EventFixItemViewModel(EventFix fix, Func<EventFixItemViewModel, Task> run, Func<bool> isAnyRunning)
        {
            _fix = fix;
            _run = run;
            _isAnyRunning = isAnyRunning;
            IsEnabled = !fix.RequiresAdmin || AppInfo.IsAdministrator;
            RunCommand = new AsyncRelayCommand(() => run(this), () => CanRun);
        }

        public EventFix Fix => _fix;
        public string Id => _fix.Id;
        public string Title => "Corrigir: " + _fix.Title;
        public string Description => _fix.Description;
        public string Risk => _fix.Risk;
        public bool RequiresAdmin => _fix.RequiresAdmin;
        public bool RequiresRestart => _fix.RequiresRestart;
        public bool NeedsConfirmation => _fix.NeedsConfirmation;
        public bool IsEnabled { get; }
        public bool IsLowRisk => _fix.RiskLevel == FixRisk.None || _fix.RiskLevel == FixRisk.Low;

        public bool CanRun => IsEnabled && !_isAnyRunning();

        public string DisabledReason => IsEnabled ? null : "Requer administrador — execute o aplicativo como administrador para habilitar este reparo.";

        public string ToolTipText => IsEnabled
            ? $"{_fix.Description}\n\nRisco: {_fix.Risk}" + (_fix.RequiresRestart ? "\n\nRequer reiniciar o computador." : string.Empty)
            : DisabledReason;

        public ObservableCollection<ConsoleLineViewModel> OutputLines { get; } = new ObservableCollection<ConsoleLineViewModel>();

        public IAsyncRelayCommand RunCommand { get; }

        public void NotifyCanRunChanged() => RunCommand.NotifyCanExecuteChanged();

        public void AppendLine(OutputLine line)
        {
            if (line.ProgressPercent.HasValue)
            {
                Progress = Math.Max(0, Math.Min(100, line.ProgressPercent.Value));
                ProgressText = Progress + "%";
            }
            var last = OutputLines.Count > 0 ? OutputLines[OutputLines.Count - 1] : null;
            var item = new ConsoleLineViewModel(line.Text, line.IsError, line.IsProgress);
            if (line.IsProgress && last != null && last.IsProgress)
                OutputLines[OutputLines.Count - 1] = item;
            else
                OutputLines.Add(item);
        }
    }

    /// <summary>Filtro da aba Erros do sistema.</summary>
    public enum EventFilter
    {
        Todos,
        Corrigiveis,
        Hardware,
        Informativos,
    }

    /// <summary>Ordenação da aba Erros do sistema.</summary>
    public enum EventSort
    {
        PorGravidade,
        PorQuantidade,
    }

    /// <summary>ViewModel de um item de erro do sistema com diagnóstico, correções e estado de execução.</summary>
    public sealed partial class EventItemViewModel : ObservableObject
    {
        private CancellationTokenSource _cts;
        private readonly object _fixGate = new object();
        private readonly IDialogService _dialogs;
        private readonly Func<bool> _isAnyRunning;
        private readonly Action _notifyOthers;

        [ObservableProperty]
        private bool _isFixRunning;

        [ObservableProperty]
        private EventFixResultViewModel _fixResult;

        public EventItemViewModel(EventSummary summary, IDialogService dialogs, Func<bool> isAnyRunning, Action notifyOthers)
        {
            Summary = summary;
            _dialogs = dialogs;
            _isAnyRunning = isAnyRunning;
            _notifyOthers = notifyOthers;

            // Diagnóstico a partir do catálogo
            Diagnosis = EventFixCatalog.Diagnosis(summary);

            // Status visual baseado na classificação
            Status = Diagnosis.Classification == EventDiagnosisClass.Hardware ? HealthStatus.Critical
                : Diagnosis.Classification == EventDiagnosisClass.Informativo ? HealthStatus.Info
                : HealthStatus.Warning;

            // Monta os fix items
            var rawFixes = EventFixCatalog.For(summary);
            Fixes = rawFixes
                .Select(f => new EventFixItemViewModel(f, item => RunFixAsync(item), isAnyRunning))
                .ToList();

            HasFixes = Fixes.Count > 0;
            HasGuidance = !HasFixes && !string.IsNullOrWhiteSpace(Diagnosis.Guidance);

            CancelFixCommand = new RelayCommand(CancelFix, () => IsFixRunning);
            CopyDetailsCommand = new RelayCommand(CopyDetails);
            OpenEventViewerCommand = new RelayCommand(OpenEventViewer);
            ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        }

        public EventSummary Summary { get; private set; }
        public EventDiagnosis Diagnosis { get; private set; }
        public HealthStatus Status { get; }
        public IReadOnlyList<EventFixItemViewModel> Fixes { get; }
        public bool HasFixes { get; }
        public bool HasGuidance { get; }

        public string Title => Diagnosis.Title;
        public string Subject => Summary.Subject == null ? null : "Origem: " + Summary.Subject;
        public string Detail =>
            $"{Summary.Provider} · evento {Summary.EventId} · log {Summary.Log} · {Summary.Count}× · última em {Summary.Last:dd/MM HH:mm}"
            + (Summary.Count > 1 ? $" · primeira em {Summary.First:dd/MM HH:mm}" : string.Empty);
        public string Message => Summary.Message ?? "(sem descrição)";
        public string GuidanceText => Diagnosis.Guidance;

        public bool IsFixable => HasFixes;
        public bool IsHardware => Diagnosis.Classification == EventDiagnosisClass.Hardware;
        public bool IsInformativo => Diagnosis.Classification == EventDiagnosisClass.Informativo;

        public IRelayCommand CancelFixCommand { get; }
        public IRelayCommand CopyDetailsCommand { get; }
        public IRelayCommand OpenEventViewerCommand { get; }
        public IAsyncRelayCommand ReloadCommand { get; }

        // Callback para quando o cartão precisa recarregar (injetado por RepairViewModel)
        public Func<Task> ReloadCallback { get; set; }

        public bool IsAnyFixLowRiskNoRestart =>
            Fixes.Any(f => f.IsLowRisk && !f.RequiresRestart);

        private async Task RunFixAsync(EventFixItemViewModel fixItem, bool fromBatch = false)
        {
            lock (_fixGate)
            {
                if ((!fromBatch && !fixItem.CanRun) || !fixItem.IsEnabled || IsFixRunning) return;
                IsFixRunning = true;
            }

            // Confirmação quando necessário
            if (fixItem.NeedsConfirmation)
            {
                var msg = fixItem.Description
                    + $"\n\nRisco: {fixItem.Risk}"
                    + (fixItem.RequiresRestart ? "\n\nSerá preciso reiniciar o computador para concluir." : string.Empty);
                if (!await _dialogs.ConfirmAsync(fixItem.Fix.Title, msg, "Corrigir"))
                {
                    IsFixRunning = false;
                    return;
                }
            }

            var localCts = new CancellationTokenSource();
            _cts = localCts;
            FixResult = null;
            fixItem.IsRunning = true;
            fixItem.Progress = 0;
            fixItem.ProgressText = "0%";
            fixItem.OutputLines.Clear();
            _notifyOthers?.Invoke();

            var dispatcher = Application.Current.Dispatcher;
            try
            {
                var result = await fixItem.Fix.RunAsync(
                    line => dispatcher.BeginInvoke(new Action(() => fixItem.AppendLine(line))),
                    localCts.Token, fixItem.NeedsConfirmation);

                FixResult = new EventFixResultViewModel
                {
                    Success = result.Success,
                    Summary = result.Summary,
                    Timestamp = DateTime.Now,
                };
            }
            catch (OperationCanceledException)
            {
                FixResult = new EventFixResultViewModel { Success = false, Summary = "Interrompido pelo usuário.", Timestamp = DateTime.Now };
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao executar fix '{fixItem.Id}'", ex);
                FixResult = new EventFixResultViewModel { Success = false, Summary = "Erro: " + ex.Message, Timestamp = DateTime.Now };
            }
            finally
            {
                fixItem.IsRunning = false;
                IsFixRunning = false;
                if (ReferenceEquals(_cts, localCts)) _cts = null;
                localCts.Dispose();
                _notifyOthers?.Invoke();
                CancelFixCommand.NotifyCanExecuteChanged();
            }
        }

        internal Task RunBatchFixAsync(EventFixItemViewModel fixItem) => RunFixAsync(fixItem, true);

        private void CancelFix() => _cts?.Cancel();

        private void CopyDetails()
        {
            try
            {
                var text = $"{Title}\n{Detail}\n\nDiagnóstico: {Diagnosis.PlainText}\nEvidência: {Diagnosis.Evidence}\n\nMensagem do Windows:\n{Message}";
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível copiar: " + ex.Message);
            }
        }

        private void OpenEventViewer()
        {
            try
            {
                Process.Start(new ProcessStartInfo("eventvwr.msc") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível abrir o Visualizador de Eventos: " + ex.Message);
            }
        }

        private async Task ReloadAsync()
        {
            if (ReloadCallback != null)
                await ReloadCallback();
        }

        partial void OnIsFixRunningChanged(bool value)
        {
            CancelFixCommand.NotifyCanExecuteChanged();
            foreach (var fix in Fixes) fix.NotifyCanRunChanged();
        }
    }

    public sealed class CrashItemViewModel
    {
        public CrashItemViewModel(CrashRecord record)
        {
            Record = record;
        }

        public CrashRecord Record { get; }

        public HealthStatus Status => Record.Code != null ? HealthStatus.Critical : HealthStatus.Warning;

        public string Title => Record.Code != null ? $"{Record.Name} ({Record.Code})" : Record.Name;

        public string When => Record.When.ToString("dd/MM/yyyy HH:mm");

        public string Explanation => Record.Explanation;

        public string Detail => Format.Join(" · ",
            string.IsNullOrEmpty(Record.Parameters) ? null : "Parâmetros: " + Record.Parameters,
            Record.DumpFile == null ? null : "Despejo: " + Record.DumpFile);
    }

    public sealed class DriverItemViewModel
    {
        private static readonly TimeSpan OldAge = TimeSpan.FromDays(365 * 3);

        public DriverItemViewModel(DriverInfo driver)
        {
            Driver = driver;
        }

        public DriverInfo Driver { get; }

        // Drivers só de INF (ex.: chipset Intel) usam a data fictícia 1968 de propósito, para não substituir drivers reais
        private bool HasRealDate => Driver.Date.HasValue && Driver.Date.Value.Year >= 2000;

        public bool IsOld => HasRealDate && DateTime.Now - Driver.Date.Value > OldAge;

        public string DateText => HasRealDate ? Driver.Date.Value.ToString("dd/MM/yyyy") : "não informada";
    }

    public partial class RepairViewModel : ObservableObject, IActivatable
    {
        private const int MaxConsoleLines = 5000;
        private const int EventDays = 7;
        private const int CrashDays = 365;

        private readonly IDialogService _dialogs;
        private CancellationTokenSource _cts;
        private RepairTool _running;
        private bool _eventsLoaded;
        private bool _crashesLoaded;
        private bool _devicesLoaded;
        private IReadOnlyList<DriverInfo> _allDrivers = new DriverInfo[0];
        private IReadOnlyList<EventItemViewModel> _allEvents = new List<EventItemViewModel>();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
        private bool _isRunning;

        [ObservableProperty]
        private int _progress;

        [ObservableProperty]
        private string _progressText = "0%";

        [ObservableProperty]
        private string _consoleTitle = "Saída";

        [ObservableProperty]
        private string _resultText;

        [ObservableProperty]
        private HealthStatus _resultStatus = HealthStatus.Info;

        [ObservableProperty]
        private int _selectedTab;

        [ObservableProperty]
        private string _eventsStatus;

        [ObservableProperty]
        private string _crashesStatus;

        [ObservableProperty]
        private string _devicesStatus;

        [ObservableProperty]
        private bool _showMicrosoftDrivers;

        // ---------- Erros do sistema — filtros, busca e ordenação ----------

        [ObservableProperty]
        private EventFilter _eventFilter = EventFilter.Todos;

        [ObservableProperty]
        private EventSort _eventSort = EventSort.PorGravidade;

        [ObservableProperty]
        private string _eventSearch = string.Empty;

        [ObservableProperty]
        private string _eventsSummaryText;

        [ObservableProperty]
        private bool _isEventBatchRunning;

        // Propriedades de filtro expostas como bool para binding via RadioButton/ToggleButton
        public bool FilterTodos
        {
            get => EventFilter == EventFilter.Todos;
            set { if (value) EventFilter = EventFilter.Todos; }
        }

        public bool FilterCorrigiveis
        {
            get => EventFilter == EventFilter.Corrigiveis;
            set { if (value) EventFilter = EventFilter.Corrigiveis; }
        }

        public bool FilterHardware
        {
            get => EventFilter == EventFilter.Hardware;
            set { if (value) EventFilter = EventFilter.Hardware; }
        }

        public bool FilterInformativos
        {
            get => EventFilter == EventFilter.Informativos;
            set { if (value) EventFilter = EventFilter.Informativos; }
        }

        public bool SortPorGravidade
        {
            get => EventSort == EventSort.PorGravidade;
            set { if (value) EventSort = EventSort.PorGravidade; }
        }

        public bool SortPorQuantidade
        {
            get => EventSort == EventSort.PorQuantidade;
            set { if (value) EventSort = EventSort.PorQuantidade; }
        }

        public RepairViewModel(IDialogService dialogs)
        {
            _dialogs = dialogs;
            ToolGroups = RepairCatalog.Tools()
                .GroupBy(t => t.Group)
                .Select(g => new RepairToolGroupViewModel(g.Key, g.Select(t => new RepairToolItemViewModel(t, RunToolAsync, () => IsRunning))))
                .ToList();
            Shortcuts = RepairCatalog.Shortcuts();
        }

        public IReadOnlyList<RepairToolGroupViewModel> ToolGroups { get; }

        public IReadOnlyList<WindowsShortcut> Shortcuts { get; }

        public ObservableCollection<ConsoleLineViewModel> ConsoleLines { get; } = new ObservableCollection<ConsoleLineViewModel>();

        public ObservableCollection<EventItemViewModel> Events { get; } = new ObservableCollection<EventItemViewModel>();

        public ObservableCollection<CrashItemViewModel> Crashes { get; } = new ObservableCollection<CrashItemViewModel>();

        public ObservableCollection<DeviceProblem> DeviceProblems { get; } = new ObservableCollection<DeviceProblem>();

        public ObservableCollection<DriverItemViewModel> Drivers { get; } = new ObservableCollection<DriverItemViewModel>();

        public bool HasResult => !string.IsNullOrEmpty(ResultText);

        partial void OnResultTextChanged(string value) => OnPropertyChanged(nameof(HasResult));

        partial void OnIsRunningChanged(bool value)
        {
            foreach (var item in ToolGroups.SelectMany(g => g.Items)) item.RunCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedTabChanged(int value) => _ = LoadTabAsync(value);

        partial void OnShowMicrosoftDriversChanged(bool value) => FillDrivers();

        partial void OnEventFilterChanged(EventFilter value)
        {
            OnPropertyChanged(nameof(FilterTodos));
            OnPropertyChanged(nameof(FilterCorrigiveis));
            OnPropertyChanged(nameof(FilterHardware));
            OnPropertyChanged(nameof(FilterInformativos));
            ApplyEventsFilter();
        }

        partial void OnEventSortChanged(EventSort value)
        {
            OnPropertyChanged(nameof(SortPorGravidade));
            OnPropertyChanged(nameof(SortPorQuantidade));
            ApplyEventsFilter();
        }

        partial void OnEventSearchChanged(string value) => ApplyEventsFilter();
        public void OnActivated() => _ = LoadTabAsync(SelectedTab);

        public void OnDeactivated()
        {
        }

        private Task LoadTabAsync(int tab)
        {
            switch (tab)
            {
                case 1 when !_eventsLoaded: return LoadEventsAsync();
                case 2 when !_crashesLoaded: return LoadCrashesAsync();
                case 3 when !_devicesLoaded: return LoadDevicesAsync();
                default: return Task.CompletedTask;
            }
        }

        // ---------- Ferramentas ----------

        private async Task RunToolAsync(RepairToolItemViewModel item)
        {
            var tool = item.Tool;
            if (tool.NeedsConfirmation)
            {
                var message = tool.Description + (tool.RequiresRestart ? "\n\nSerá preciso reiniciar o computador para concluir." : string.Empty)
                                               + "\n\nDuração estimada: " + tool.Duration + ".";
                if (!await _dialogs.ConfirmAsync(tool.Name, message, "Executar")) return;
            }

            _running = tool;
            var localCts = new CancellationTokenSource();
            _cts = localCts;
            IsRunning = true;
            ResultText = null;
            Progress = 0;
            ProgressText = "0%";
            ConsoleLines.Clear();
            ConsoleTitle = tool.Name;
            var dispatcher = Application.Current.Dispatcher;

            try
            {
                var result = await tool.RunAsync(line => dispatcher.BeginInvoke(new Action(() => AppendLine(line))), localCts.Token);
                ResultText = result.Summary;
                ResultStatus = result.Success ? HealthStatus.Ok : HealthStatus.Warning;
            }
            catch (OperationCanceledException)
            {
                ResultText = "Interrompido pelo usuário.";
                ResultStatus = HealthStatus.Warning;
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao executar '{tool.Id}'", ex);
                ResultText = "Erro: " + ex.Message;
                ResultStatus = HealthStatus.Critical;
            }
            finally
            {
                IsRunning = false;
                _running = null;
                if (ReferenceEquals(_cts, localCts)) _cts = null;
                localCts.Dispose();
            }
        }

        private void AppendLine(OutputLine line)
        {
            if (line.ProgressPercent.HasValue)
            {
                Progress = Math.Max(0, Math.Min(100, line.ProgressPercent.Value));
                ProgressText = Progress + "%";
            }
            var last = ConsoleLines.Count > 0 ? ConsoleLines[ConsoleLines.Count - 1] : null;
            var item = new ConsoleLineViewModel(line.Text, line.IsError, line.IsProgress);

            // Barra de progresso: substitui a linha anterior de progresso em vez de acumular
            if (line.IsProgress && last != null && last.IsProgress) ConsoleLines[ConsoleLines.Count - 1] = item;
            else ConsoleLines.Add(item);

            while (ConsoleLines.Count > MaxConsoleLines) ConsoleLines.RemoveAt(0);
        }

        private bool CanCancel() => IsRunning;

        [RelayCommand(CanExecute = nameof(CanCancel))]
        private async Task CancelAsync()
        {
            if (_running?.UnsafeToCancel == true
                && !await _dialogs.ConfirmAsync("Interromper?", $"Interromper \"{_running.Name}\" no meio pode deixar o reparo incompleto. Ele pode ser executado de novo depois.", "Interromper"))
                return;
            _cts?.Cancel();
        }

        [RelayCommand]
        private void CopyConsole()
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, ConsoleLines.Select(l => l.Text)));
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível copiar a saída: " + ex.Message);
            }
        }

        [RelayCommand]
        private void OpenShortcut(WindowsShortcut shortcut)
        {
            if (shortcut == null) return;
            try
            {
                Process.Start(new ProcessStartInfo(shortcut.Command, shortcut.Arguments ?? string.Empty) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn($"Não foi possível abrir {shortcut.Command}: {ex.Message}");
                ResultText = $"Não foi possível abrir {shortcut.Name}: {ex.Message}";
                ResultStatus = HealthStatus.Warning;
            }
        }

        // ---------- Erros do sistema ----------

        private bool IsAnyEventFixRunning() =>
            _allEvents.Any(e => e.IsFixRunning) || IsEventBatchRunning;

        [RelayCommand]
        private async Task LoadEventsAsync()
        {
            _eventsLoaded = true;
            EventsStatus = "Lendo os logs do Windows…";
            try
            {
                var summary = await Task.Run(() => EventLogAnalyzer.Analyze(EventDays, CancellationToken.None));

                // Cria os view models com acesso ao serviço de diálogo e ao estado de execução
                _allEvents = summary
                    .Select(s =>
                    {
                        var vm = new EventItemViewModel(s, _dialogs, IsAnyEventFixRunning, NotifyAllEventFixes);
                        vm.ReloadCallback = LoadEventsAsync;
                        return vm;
                    })
                    .ToList();

                ApplyEventsFilter();
                UpdateEventsSummary(summary);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao ler eventos", ex);
                EventsStatus = "Erro ao ler os logs: " + ex.Message;
            }
        }

        private void UpdateEventsSummary(IReadOnlyList<EventSummary> summary)
        {
            var totalCount = summary.Sum(s => s.Count);
            var totalTypes = summary.Count;
            var fixable = _allEvents.Count(e => e.HasFixes);
            EventsStatus = summary.Count == 0
                ? $"Nenhum erro nos últimos {EventDays} dias."
                : $"{totalCount} erros em {totalTypes} tipos nos últimos {EventDays} dias · {fixable} corrigível(is).";
            EventsSummaryText = summary.Count == 0
                ? null
                : $"{totalCount} erros em {totalTypes} tipos · {fixable} corrigível(is)";
        }

        private void ApplyEventsFilter()
        {
            var search = (EventSearch ?? string.Empty).Trim();
            var filtered = _allEvents.Where(e =>
            {
                // Filtro de categoria
                switch (EventFilter)
                {
                    case EventFilter.Corrigiveis when !e.HasFixes: return false;
                    case EventFilter.Hardware when !e.IsHardware: return false;
                    case EventFilter.Informativos when !e.IsInformativo: return false;
                }

                // Busca por texto
                if (search.Length > 0)
                {
                    var hay = $"{e.Title} {e.Subject} {e.Summary.Provider} {e.Summary.EventId} {e.Diagnosis.PlainText}";
                    if (hay.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) return false;
                }

                return true;
            });

            // Ordenação
            IOrderedEnumerable<EventItemViewModel> ordered;
            if (EventSort == EventSort.PorQuantidade)
                ordered = filtered.OrderByDescending(e => e.Summary.Count).ThenBy(e => e.Title);
            else
                ordered = filtered
                    .OrderBy(e => e.Diagnosis.Classification) // Real < Hardware < Informativo (por valor enum; Real=0, Hardware=2, Informativo=1)
                    .ThenByDescending(e => e.Summary.IsCritical)
                    .ThenByDescending(e => e.Summary.Count);

            Events.Clear();
            foreach (var e in ordered) Events.Add(e);
        }

        private void NotifyAllEventFixes()
        {
            foreach (var e in _allEvents)
                foreach (var fix in e.Fixes)
                    fix.NotifyCanRunChanged();
        }

        /// <summary>Executa em fila todos os fixes de risco baixo/nenhum sem reinício, com uma única confirmação.</summary>
        [RelayCommand]
        private async Task FixRecommendedAsync()
        {
            var candidates = _allEvents
                .Where(e => e.HasFixes)
                .SelectMany(e => e.Fixes.Select(f => (Event: e, Fix: f)))
                .Where(x => EventFixPolicy.CanBeRecommended(x.Fix.Fix)
                    && x.Fix.IsLowRisk && !x.Fix.RequiresRestart && x.Fix.IsEnabled)
                .ToList();

            if (candidates.Count == 0)
            {
                EventsStatus = "Nenhuma correção de risco baixo disponível.";
                return;
            }

            // Confirmação única listando tudo
            var list = string.Join("\n", candidates.Select(x => $"• {x.Fix.Fix.Title} ({x.Event.Title})"));
            var msg = $"Serão executados {candidates.Count} reparo(s) de risco baixo:\n\n{list}\n\nNenhum requer reiniciar o computador.";
            if (!await _dialogs.ConfirmAsync("Corrigir recomendados", msg, "Corrigir tudo")) return;

            IsEventBatchRunning = true;
            NotifyAllEventFixes();
            try
            {
                foreach (var (eventVm, fixVm) in candidates)
                {
                    // Revalida cada item no instante da execução, depois da confirmação única.
                    if (!EventFixPolicy.CanBeRecommended(fixVm.Fix)
                        || !fixVm.IsEnabled || fixVm.RequiresRestart || fixVm.NeedsConfirmation
                        || fixVm.IsRunning || eventVm.IsFixRunning)
                        continue;
                    await eventVm.RunBatchFixAsync(fixVm);
                }
            }
            finally
            {
                IsEventBatchRunning = false;
                NotifyAllEventFixes();
                // Recarrega a lista para refletir o estado atual
                _eventsLoaded = false;
                await LoadEventsAsync();
            }
        }

        // ---------- Telas azuis ----------

        [RelayCommand]
        private async Task LoadCrashesAsync()
        {
            _crashesLoaded = true;
            CrashesStatus = "Procurando telas azuis e desligamentos inesperados…";
            try
            {
                var crashes = await Task.Run(() => CrashAnalyzer.Analyze(CrashDays, CancellationToken.None));
                Crashes.Clear();
                foreach (var crash in crashes) Crashes.Add(new CrashItemViewModel(crash));
                var blue = crashes.Count(c => c.Code != null);
                CrashesStatus = crashes.Count == 0
                    ? "Nenhuma tela azul ou desligamento inesperado no último ano."
                    : $"{blue} tela(s) azul(is) e {crashes.Count - blue} desligamento(s) inesperado(s) no último ano."
                      + (AppInfo.IsAdministrator ? string.Empty : " Execute como administrador para ler também os arquivos de despejo.");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao analisar telas azuis", ex);
                CrashesStatus = "Erro na análise: " + ex.Message;
            }
        }

        [RelayCommand]
        private void OpenMinidumpFolder()
        {
            try
            {
                Process.Start("explorer.exe", $"\"{CrashAnalyzer.MinidumpFolder}\"");
            }
            catch (Exception ex)
            {
                CrashesStatus = "Não foi possível abrir a pasta: " + ex.Message;
            }
        }

        // ---------- Dispositivos e drivers ----------

        [RelayCommand]
        private async Task LoadDevicesAsync()
        {
            _devicesLoaded = true;
            DevicesStatus = "Consultando dispositivos e drivers…";
            try
            {
                var (problems, drivers) = await Task.Run(() => (DeviceAnalyzer.ListProblems(), DeviceAnalyzer.ListDrivers()));
                DeviceProblems.Clear();
                foreach (var problem in problems) DeviceProblems.Add(problem);
                _allDrivers = drivers;
                FillDrivers();

                var failing = problems.Count(p => !p.IsDisabled);
                var old = drivers.Count(d => !d.IsMicrosoft && new DriverItemViewModel(d).IsOld);
                DevicesStatus = (failing == 0 ? "Nenhum dispositivo com erro." : $"{failing} dispositivo(s) com erro.")
                                + $" {drivers.Count} drivers instalados" + (old > 0 ? $", {old} de fabricantes com mais de 3 anos." : ".");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao listar dispositivos", ex);
                DevicesStatus = "Erro ao consultar: " + ex.Message;
            }
        }

        private void FillDrivers()
        {
            Drivers.Clear();
            foreach (var driver in _allDrivers.Where(d => ShowMicrosoftDrivers || !d.IsMicrosoft))
                Drivers.Add(new DriverItemViewModel(driver));
        }

        [RelayCommand]
        private void OpenDeviceManager() => OpenShortcut(Shortcuts.First(s => s.Command == "devmgmt.msc"));
    }
}
