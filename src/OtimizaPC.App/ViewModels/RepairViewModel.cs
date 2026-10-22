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

    public sealed class EventItemViewModel
    {
        public EventItemViewModel(EventSummary summary)
        {
            Summary = summary;
            Status = summary.Importance == EventImportance.High ? HealthStatus.Critical
                : summary.Importance == EventImportance.Benign ? HealthStatus.Info
                : HealthStatus.Warning;
        }

        public EventSummary Summary { get; }

        public HealthStatus Status { get; }

        public string Title => Summary.Explanation ?? $"{Summary.Provider} (evento {Summary.EventId})";

        public string Subject => Summary.Subject == null ? null : "Programa: " + Summary.Subject;

        public string Detail =>
            $"{Summary.Provider} · evento {Summary.EventId} · log {Summary.Log} · {Summary.Count}× · última em {Summary.Last:dd/MM HH:mm}"
            + (Summary.Count > 1 ? $" · primeira em {Summary.First:dd/MM HH:mm}" : string.Empty);

        public string Message => Summary.Message ?? "(sem descrição)";
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

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
        private bool _isRunning;

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
            _cts = new CancellationTokenSource();
            IsRunning = true;
            ResultText = null;
            ConsoleLines.Clear();
            ConsoleTitle = tool.Name;
            var dispatcher = Application.Current.Dispatcher;

            try
            {
                var result = await tool.RunAsync(line => dispatcher.BeginInvoke(new Action(() => AppendLine(line))), _cts.Token);
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
            }
        }

        private void AppendLine(OutputLine line)
        {
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

        [RelayCommand]
        private async Task LoadEventsAsync()
        {
            _eventsLoaded = true;
            EventsStatus = "Lendo os logs do Windows…";
            try
            {
                var summary = await Task.Run(() => EventLogAnalyzer.Analyze(EventDays, CancellationToken.None));
                Events.Clear();
                foreach (var item in summary) Events.Add(new EventItemViewModel(item));
                var important = summary.Count(s => s.Importance == EventImportance.High);
                EventsStatus = summary.Count == 0
                    ? $"Nenhum erro nos últimos {EventDays} dias."
                    : $"{summary.Sum(s => s.Count)} erros em {summary.Count} tipos nos últimos {EventDays} dias"
                      + (important > 0 ? $" · {important} tipo(s) indicam problema de hardware ou estabilidade." : ".");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao ler eventos", ex);
                EventsStatus = "Erro ao ler os logs: " + ex.Message;
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
