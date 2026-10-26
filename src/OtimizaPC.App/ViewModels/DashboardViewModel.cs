using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.Core;
using OtimizaPC.Core.Models;
using OtimizaPC.Hardware;
using OtimizaPC.Hardware.Sensors;
using Wpf.Ui.Controls;

namespace OtimizaPC.App.ViewModels
{
    public partial class DashboardViewModel : ObservableObject, IActivatable, IDisposable
    {
        private const int NetworkHistoryLength = 60;

        private static readonly HashSet<string> NavigationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dashboard", "hardware", "sensors", "disk", "startup",
            "cleanup", "optimizations", "repair", "apps", "settings", "health",
        };

        private readonly SensorService _sensors;
        private readonly HardwareInfoService _hardwareInfo;
        private readonly HealthService _health;
        private readonly SystemMetrics _metrics = new SystemMetrics();
        private readonly DashboardMetrics _dashboardMetrics = new DashboardMetrics();
        private readonly Action<string> _navigate;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _healthTimer;
        private readonly Queue<double> _downloadSamples = new Queue<double>();
        private readonly Queue<double> _uploadSamples = new Queue<double>();
        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _isActive;
        private bool _disposed;
        private bool _summaryLoaded;
        private bool _healthLoaded;
        private int _tick;

        [ObservableProperty]
        private int _score;

        [ObservableProperty]
        private string _scoreLabel = "Verificando…";

        [ObservableProperty]
        private HealthStatus _scoreStatus = HealthStatus.Info;

        [ObservableProperty]
        private bool _isCheckingHealth;

        [ObservableProperty]
        private string _sensorMessage;

        [ObservableProperty]
        private bool _isSummaryLoading;

        [ObservableProperty]
        private string _summaryUpdatedText;

        [ObservableProperty]
        private string _healthUpdatedText;

        [ObservableProperty]
        private string _lastMetricsUpdatedText;

        [ObservableProperty]
        private bool _areHealthDetailsVisible = true;

        [ObservableProperty]
        private bool _hasTemperature;

        [ObservableProperty]
        private double _cpuTemperature;

        [ObservableProperty]
        private string _cpuTemperatureText = "-";

        [ObservableProperty]
        private double _gpuTemperature;

        [ObservableProperty]
        private string _gpuTemperatureText = "-";

        [ObservableProperty]
        private HealthStatus _temperatureStatus = HealthStatus.Ok;

        [ObservableProperty]
        private double[] _networkDownloadHistory = new double[0];

        [ObservableProperty]
        private double[] _networkUploadHistory = new double[0];

        [ObservableProperty]
        private double _networkHistoryMaximum = 1024;

        [ObservableProperty]
        private string _networkDownloadText;

        [ObservableProperty]
        private string _networkUploadText;

        public SensorSetupViewModel SensorSetup { get; }

        public DashboardViewModel(SensorService sensors, HardwareInfoService hardwareInfo, HealthService health,
            SensorSetupViewModel sensorSetup, Action<string> navigate = null)
        {
            SensorSetup = sensorSetup;
            _sensors = sensors;
            _hardwareInfo = hardwareInfo;
            _health = health;
            _navigate = navigate;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnMetricsTick;

            // Saúde e resumo mudam pouco: atualizar a cada minuto evita WMI/verificações a cada segundo
            _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _healthTimer.Tick += OnHealthTick;

            Cards = new ObservableCollection<MetricCard> { Cpu, Memory, Gpu, Disk, Network, Uptime };
        }

        public MetricCard Cpu { get; } = new MetricCard(
            "Processador", SymbolRegular.DeveloperBoard24,
            destination: "sensors", tooltip: "Abrir Sensores (CPU e temperatura)");

        public MetricCard Memory { get; } = new MetricCard(
            "Memória RAM", SymbolRegular.Ram20,
            destination: "hardware", tooltip: "Abrir Hardware (memória RAM)");

        public MetricCard Gpu { get; } = new MetricCard(
            "Placa de vídeo", SymbolRegular.Video24,
            destination: "sensors", tooltip: "Abrir Sensores (placa de vídeo)");

        public MetricCard Disk { get; } = new MetricCard(
            "Disco do sistema", SymbolRegular.Storage24, showChart: false,
            destination: "disk", tooltip: "Abrir Espaço em disco");

        public MetricCard Network { get; } = new MetricCard(
            "Rede", SymbolRegular.NetworkCheck24, maximum: double.NaN,
            destination: "sensors", tooltip: "Abrir Sensores (rede)");

        public MetricCard Uptime { get; } = new MetricCard(
            "Tempo ligado", SymbolRegular.Clock24, showChart: false,
            destination: "startup", tooltip: "Abrir Inicialização");

        public ObservableCollection<MetricCard> Cards { get; }

        public ObservableCollection<HealthCheck> Checks { get; } = new ObservableCollection<HealthCheck>();

        public ObservableCollection<InfoEntry> Summary { get; } = new ObservableCollection<InfoEntry>();

        /// <summary>Uso de cada processador lógico.</summary>
        public ObservableCollection<CpuCoreUsage> CpuCores { get; } = new ObservableCollection<CpuCoreUsage>();

        /// <summary>Processos que mais consomem CPU.</summary>
        public ObservableCollection<ProcessUsageItem> TopProcessesByCpu { get; } = new ObservableCollection<ProcessUsageItem>();

        /// <summary>Processos que mais consomem memória.</summary>
        public ObservableCollection<ProcessUsageItem> TopProcessesByMemory { get; } = new ObservableCollection<ProcessUsageItem>();

        /// <summary>Uso das unidades fixas.</summary>
        public ObservableCollection<DiskUsageItem> Disks { get; } = new ObservableCollection<DiskUsageItem>();

        public bool HasSensorMessage => !string.IsNullOrEmpty(SensorMessage);

        partial void OnSensorMessageChanged(string value) => OnPropertyChanged(nameof(HasSensorMessage));

        public void OnActivated()
        {
            if (_disposed) return;

            // Cada ativação ganha um token novo: o que estava em voo é cancelado e descartado
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _isActive = true;

            _timer.Start();
            _healthTimer.Start();
            _ = RefreshMetricsAsync();
            if (!_summaryLoaded) _ = LoadSummaryAsync();
            if (!_healthLoaded) _ = RunHealthCheckAsync();
        }

        public void OnDeactivated()
        {
            _isActive = false;
            _timer.Stop();
            _healthTimer.Stop();

            var cts = _cts;
            _cts = null;
            if (cts == null) return;

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Já liberado por uma desativação anterior
            }
            finally
            {
                cts.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            OnDeactivated();
            _timer.Tick -= OnMetricsTick;
            _healthTimer.Tick -= OnHealthTick;
        }

        /// <summary>Navega para a página do card; "health" permanece no painel e abre os detalhes.</summary>
        [RelayCommand]
        private void Navigate(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination)) return;

            var key = destination.Trim().ToLowerInvariant();
            if (!NavigationKeys.Contains(key)) return;

            if (key == "health")
            {
                AreHealthDetailsVisible = true;
                _navigate?.Invoke("dashboard");
                return;
            }

            _navigate?.Invoke(key);
        }

        [RelayCommand]
        private void ToggleHealthDetails() => AreHealthDetailsVisible = !AreHealthDetailsVisible;

        private async void OnMetricsTick(object sender, EventArgs e) => await RefreshMetricsAsync();

        private async void OnHealthTick(object sender, EventArgs e) => await RefreshHealthAndSummaryAsync();

        /// <summary>Token da ativação corrente; nunca lança se o CTS já foi liberado.</summary>
        private CancellationToken CurrentToken
        {
            get
            {
                var cts = _cts;
                if (cts == null) return CancellationToken.None;
                try
                {
                    return cts.Token;
                }
                catch (ObjectDisposedException)
                {
                    return CancellationToken.None;
                }
            }
        }

        /// <summary>True quando a tela saiu (ou o token foi cancelado) e nada mais deve ser publicado.</summary>
        private bool IsStale(CancellationToken token) => _disposed || !_isActive || token.IsCancellationRequested;

        private async Task RefreshHealthAndSummaryAsync()
        {
            if (!_isActive) return;
            await RunHealthCheckAsync();
            await LoadSummaryAsync();
        }

        private async Task LoadSummaryAsync()
        {
            if (IsSummaryLoading || !_isActive) return;
            var token = CurrentToken;
            IsSummaryLoading = true;
            try
            {
                var entries = await _hardwareInfo.GetSummaryAsync();
                if (IsStale(token)) return;

                Summary.Clear();
                foreach (var entry in entries) Summary.Add(entry);
                SummaryUpdatedText = "Atualizado às " + DateTime.Now.ToString("HH:mm");
                _summaryLoaded = true;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao carregar o resumo do sistema", ex);
            }
            finally
            {
                IsSummaryLoading = false;
            }
        }

        [RelayCommand]
        private async Task RunHealthCheckAsync()
        {
            if (IsCheckingHealth || !_isActive) return;
            var token = CurrentToken;
            IsCheckingHealth = true;
            try
            {
                // Aguarda os sensores para incluir a temperatura na verificação
                await _sensors.StartAsync();
                if (IsStale(token)) return;

                var result = await _health.RunAsync();
                if (IsStale(token)) return;

                Checks.Clear();
                foreach (var check in result.Checks.OrderByDescending(c => c.Status)) Checks.Add(check);

                Score = result.Score;
                ScoreStatus = result.Score >= 85 ? HealthStatus.Ok : result.Score >= 60 ? HealthStatus.Warning : HealthStatus.Critical;
                ScoreLabel = result.Score >= 90 ? "Ótimo" : result.Score >= 75 ? "Bom" : result.Score >= 60 ? "Precisa de atenção" : "Crítico";
                HealthUpdatedText = "Verificado às " + DateTime.Now.ToString("HH:mm");
                _healthLoaded = true;
            }
            catch (Exception ex)
            {
                Log.Error("Falha na verificação de saúde", ex);
                if (!IsStale(token)) ScoreLabel = "Erro na verificação";
            }
            finally
            {
                IsCheckingHealth = false;
            }
        }

        private async Task RefreshMetricsAsync()
        {
            if (_busy || !_isActive) return;
            _busy = true;
            var token = CurrentToken;
            try
            {
                var tick = ++_tick;
                var includeProcesses = tick % 2 == 0;

                // Discos a cada ~10 s para não acordar HDs nem pesar a UI
                var includeDisks = tick == 1 || tick % 10 == 0;

                // P/Invoke, processos e disco saem da thread do dispatcher: a UI só recebe o snapshot
                var snapshot = await Task.Run(() => CollectMetrics(includeProcesses, includeDisks), token);
                if (IsStale(token)) return;

                ApplyMetrics(snapshot);
                LastMetricsUpdatedText = "Métricas às " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (OperationCanceledException)
            {
                // Tela desativada/fechada durante a coleta
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao atualizar métricas: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private MetricsSnapshot CollectMetrics(bool includeProcesses, bool includeDisks) =>
            new MetricsSnapshot(
                _metrics.GetCpuUsage(),
                SystemMetrics.GetMemory(),
                _metrics.GetNetworkRate(),
                _sensors.Read(),
                _dashboardMetrics.GetCpuCores(),
                includeProcesses ? _dashboardMetrics.GetTopProcesses() : null,
                includeDisks ? _dashboardMetrics.GetDisks() : null,
                _dashboardMetrics.GetSystemDrive());

        private void ApplyMetrics(MetricsSnapshot snapshot)
        {
            UpdateCpu(snapshot.CpuUsage, SensorPicker.CpuTemperature(snapshot.Readings));
            UpdateMemory(snapshot.Memory);
            UpdateGpu(snapshot.Readings);
            UpdateDisk(snapshot.SystemDrive);
            UpdateNetwork(snapshot.Network);
            UpdateUptime();
            UpdateTemperatures(snapshot.Readings);
            UpdateCpuCores(snapshot.Cores);
            UpdateSensorMessage(SensorPicker.CpuTemperature(snapshot.Readings).HasValue);

            if (snapshot.Processes != null)
            {
                Replace(TopProcessesByCpu, snapshot.Processes.ByCpu);
                Replace(TopProcessesByMemory, snapshot.Processes.ByMemory);
            }

            if (snapshot.Disks != null) Replace(Disks, snapshot.Disks);
        }

        private void UpdateCpu(double usage, float? temperature)
        {
            Cpu.Value = Format.Percent(usage);
            Cpu.Push(usage);
            Cpu.Detail = temperature.HasValue
                ? "Temperatura: " + SensorReading.FormatValue(SensorKind.Temperature, temperature)
                : "Temperatura indisponível";
            Cpu.Status = temperature >= 90 ? HealthStatus.Critical : temperature >= 80 ? HealthStatus.Warning : HealthStatus.Ok;
        }

        private void UpdateMemory(MemoryUsage memory)
        {
            Memory.Value = Format.Percent(memory.Percent);
            Memory.Detail = $"{Format.Bytes(memory.Used)} de {Format.Bytes(memory.Total)}";
            Memory.Push(memory.Percent);
            Memory.Status = memory.Percent >= 95 ? HealthStatus.Critical : memory.Percent >= 85 ? HealthStatus.Warning : HealthStatus.Ok;
        }

        private void UpdateGpu(IReadOnlyList<SensorReading> readings)
        {
            var gpu = SensorPicker.MainGpuName(readings);
            if (gpu == null)
            {
                Gpu.Value = "—";
                Gpu.Detail = _sensors.State == SensorServiceState.Starting ? "Carregando sensores…" : "Sem leitura de GPU";
                return;
            }

            var load = SensorPicker.GpuLoad(readings, gpu);
            var temperature = SensorPicker.GpuTemperature(readings, gpu);
            Gpu.Value = load.HasValue ? Format.Percent(load.Value) : "—";
            Gpu.Push(load ?? 0);
            Gpu.Detail = temperature.HasValue
                ? $"{SensorReading.FormatValue(SensorKind.Temperature, temperature)} · {gpu}"
                : gpu;
            Gpu.Status = temperature >= 90 ? HealthStatus.Critical : temperature >= 83 ? HealthStatus.Warning : HealthStatus.Ok;
        }

        /// <summary>Snapshot já lido em background: aqui só formata texto para a UI.</summary>
        private void UpdateDisk(DiskUsageItem drive)
        {
            if (drive == null) return;

            Disk.Value = drive.FreeText;
            Disk.Detail = $"{drive.UsedText} de {drive.TotalText} ({drive.Name})";
            Disk.Status = drive.Status;
        }

        private void UpdateNetwork(NetworkRate rate)
        {
            Network.Value = "↓ " + Format.Rate(rate.DownloadBytesPerSecond);
            Network.Detail = "↑ " + Format.Rate(rate.UploadBytesPerSecond);
            Network.Push(rate.DownloadBytesPerSecond);

            PushSample(_downloadSamples, rate.DownloadBytesPerSecond);
            PushSample(_uploadSamples, rate.UploadBytesPerSecond);
            NetworkDownloadHistory = _downloadSamples.ToArray();
            NetworkUploadHistory = _uploadSamples.ToArray();
            NetworkHistoryMaximum = Math.Max(1024, Math.Max(
                _downloadSamples.DefaultIfEmpty(0).Max(),
                _uploadSamples.DefaultIfEmpty(0).Max()));
            NetworkDownloadText = "Baixando " + Format.Rate(rate.DownloadBytesPerSecond);
            NetworkUploadText = "Enviando " + Format.Rate(rate.UploadBytesPerSecond);
        }

        private static void PushSample(Queue<double> samples, double value)
        {
            samples.Enqueue(double.IsNaN(value) ? 0 : Math.Max(0, value));
            while (samples.Count > NetworkHistoryLength) samples.Dequeue();
        }

        private void UpdateUptime()
        {
            var uptime = SystemMetrics.GetUptime();
            Uptime.Value = Format.Duration(uptime);
            Uptime.Detail = "Desde " + Format.Date(DateTime.Now - uptime, withTime: true);
            Uptime.Status = uptime.TotalDays >= 14 ? HealthStatus.Warning : HealthStatus.Ok;
        }

        private void UpdateTemperatures(IReadOnlyList<SensorReading> readings)
        {
            var cpu = SensorPicker.CpuTemperature(readings);
            var gpuName = SensorPicker.MainGpuName(readings);
            var gpu = gpuName == null ? null : SensorPicker.GpuTemperature(readings, gpuName);

            CpuTemperature = cpu ?? 0;
            GpuTemperature = gpu ?? 0;
            CpuTemperatureText = cpu.HasValue ? SensorReading.FormatValue(SensorKind.Temperature, cpu) : "-";
            GpuTemperatureText = gpu.HasValue ? SensorReading.FormatValue(SensorKind.Temperature, gpu) : "-";
            HasTemperature = cpu.HasValue || gpu.HasValue;

            var highest = Math.Max(cpu ?? 0, gpu ?? 0);
            TemperatureStatus = highest >= 90 ? HealthStatus.Critical : highest >= 80 ? HealthStatus.Warning : HealthStatus.Ok;
        }

        /// <summary>Atualiza os itens existentes por índice (não congela na primeira amostra).</summary>
        private void UpdateCpuCores(IReadOnlyList<double> cores) => DashboardMetrics.SyncCores(CpuCores, cores);

        private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
        {
            if (target.Count == items.Count)
            {
                var same = true;
                for (var i = 0; i < items.Count; i++)
                {
                    if (!Equals(target[i], items[i])) { same = false; break; }
                }
                if (same) return;
            }

            target.Clear();
            foreach (var item in items) target.Add(item);
        }

        private void UpdateSensorMessage(bool hasCpuTemperature)
        {
            SensorSetup.RefreshInstalledState();
            SensorMessage = SensorSetup.IsBusy ? SensorSetup.Status
                : SensorAvailability.Message(_sensors.State, _sensors.ErrorMessage,
                    AppInfo.IsAdministrator, SensorSetup.IsInstalled, hasCpuTemperature);
        }

        /// <summary>Tudo que foi lido fora da thread da UI numa única amostra.</summary>
        private sealed class MetricsSnapshot
        {
            public MetricsSnapshot(
                double cpuUsage,
                MemoryUsage memory,
                NetworkRate network,
                IReadOnlyList<SensorReading> readings,
                IReadOnlyList<double> cores,
                ProcessSnapshot processes,
                IReadOnlyList<DiskUsageItem> disks,
                DiskUsageItem systemDrive)
            {
                CpuUsage = cpuUsage;
                Memory = memory;
                Network = network;
                Readings = readings;
                Cores = cores;
                Processes = processes;
                Disks = disks;
                SystemDrive = systemDrive;
            }

            public double CpuUsage { get; }

            public MemoryUsage Memory { get; }

            public NetworkRate Network { get; }

            public IReadOnlyList<SensorReading> Readings { get; }

            public IReadOnlyList<double> Cores { get; }

            public ProcessSnapshot Processes { get; }

            public IReadOnlyList<DiskUsageItem> Disks { get; }

            public DiskUsageItem SystemDrive { get; }
        }
    }
}
