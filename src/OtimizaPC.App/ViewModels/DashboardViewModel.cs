using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    public partial class DashboardViewModel : ObservableObject, IActivatable
    {
        private readonly SensorService _sensors;
        private readonly HardwareInfoService _hardwareInfo;
        private readonly HealthService _health;
        private readonly SystemMetrics _metrics = new SystemMetrics();
        private readonly DispatcherTimer _timer;
        private bool _busy;
        private bool _initialized;

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

        public DashboardViewModel(SensorService sensors, HardwareInfoService hardwareInfo, HealthService health)
        {
            _sensors = sensors;
            _hardwareInfo = hardwareInfo;
            _health = health;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += async (_, __) => await RefreshMetricsAsync();

            Cards = new ObservableCollection<MetricCard> { Cpu, Memory, Gpu, Disk, Network, Uptime };
        }

        public MetricCard Cpu { get; } = new MetricCard("Processador", SymbolRegular.DeveloperBoard24);

        public MetricCard Memory { get; } = new MetricCard("Memória RAM", SymbolRegular.Ram20);

        public MetricCard Gpu { get; } = new MetricCard("Placa de vídeo", SymbolRegular.Video24);

        public MetricCard Disk { get; } = new MetricCard("Disco do sistema", SymbolRegular.Storage24, showChart: false);

        public MetricCard Network { get; } = new MetricCard("Rede", SymbolRegular.NetworkCheck24, maximum: double.NaN);

        public MetricCard Uptime { get; } = new MetricCard("Tempo ligado", SymbolRegular.Clock24, showChart: false);

        public ObservableCollection<MetricCard> Cards { get; }

        public ObservableCollection<HealthCheck> Checks { get; } = new ObservableCollection<HealthCheck>();

        public ObservableCollection<InfoEntry> Summary { get; } = new ObservableCollection<InfoEntry>();

        public bool HasSensorMessage => !string.IsNullOrEmpty(SensorMessage);

        partial void OnSensorMessageChanged(string value) => OnPropertyChanged(nameof(HasSensorMessage));

        public void OnActivated()
        {
            _timer.Start();
            _ = RefreshMetricsAsync();
            if (_initialized) return;
            _initialized = true;
            _ = LoadSummaryAsync();
            _ = RunHealthCheckAsync();
        }

        public void OnDeactivated() => _timer.Stop();

        private async Task LoadSummaryAsync()
        {
            try
            {
                var entries = await _hardwareInfo.GetSummaryAsync();
                Summary.Clear();
                foreach (var entry in entries) Summary.Add(entry);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao carregar o resumo do sistema", ex);
            }
        }

        [RelayCommand]
        private async Task RunHealthCheckAsync()
        {
            if (IsCheckingHealth) return;
            IsCheckingHealth = true;
            try
            {
                // Aguarda os sensores para incluir a temperatura na verificação
                await _sensors.StartAsync();
                var result = await _health.RunAsync();

                Checks.Clear();
                foreach (var check in result.Checks.OrderByDescending(c => c.Status)) Checks.Add(check);

                Score = result.Score;
                ScoreStatus = result.Score >= 85 ? HealthStatus.Ok : result.Score >= 60 ? HealthStatus.Warning : HealthStatus.Critical;
                ScoreLabel = result.Score >= 90 ? "Ótimo" : result.Score >= 75 ? "Bom" : result.Score >= 60 ? "Precisa de atenção" : "Crítico";
            }
            catch (Exception ex)
            {
                Log.Error("Falha na verificação de saúde", ex);
                ScoreLabel = "Erro na verificação";
            }
            finally
            {
                IsCheckingHealth = false;
            }
        }

        private async Task RefreshMetricsAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var cpuUsage = _metrics.GetCpuUsage();
                var memory = SystemMetrics.GetMemory();
                var network = _metrics.GetNetworkRate();
                var readings = await Task.Run(() => _sensors.Read());

                UpdateCpu(cpuUsage, SensorPicker.CpuTemperature(readings));
                UpdateMemory(memory);
                UpdateGpu(readings);
                UpdateDisk();
                UpdateNetwork(network);
                UpdateUptime();
                UpdateSensorMessage(readings.Count);
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

        private void UpdateGpu(System.Collections.Generic.IReadOnlyList<SensorReading> readings)
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

        private void UpdateDisk()
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
            if (!drive.IsReady) return;
            var freePercent = drive.TotalFreeSpace * 100.0 / drive.TotalSize;
            Disk.Value = Format.Bytes(drive.TotalFreeSpace) + " livres";
            Disk.Detail = $"{Format.Percent(100 - freePercent)} usado de {Format.Bytes(drive.TotalSize)} ({drive.Name.TrimEnd('\\')})";
            Disk.Status = freePercent < 10 ? HealthStatus.Critical : freePercent < 20 ? HealthStatus.Warning : HealthStatus.Ok;
        }

        private void UpdateNetwork(NetworkRate rate)
        {
            Network.Value = "↓ " + Format.Rate(rate.DownloadBytesPerSecond);
            Network.Detail = "↑ " + Format.Rate(rate.UploadBytesPerSecond);
            Network.Push(rate.DownloadBytesPerSecond);
        }

        private void UpdateUptime()
        {
            var uptime = SystemMetrics.GetUptime();
            Uptime.Value = Format.Duration(uptime);
            Uptime.Detail = "Desde " + Format.Date(DateTime.Now - uptime, withTime: true);
            Uptime.Status = uptime.TotalDays >= 14 ? HealthStatus.Warning : HealthStatus.Ok;
        }

        private void UpdateSensorMessage(int readingCount)
        {
            switch (_sensors.State)
            {
                case SensorServiceState.Failed:
                    SensorMessage = "Não foi possível iniciar a leitura de sensores: " + _sensors.ErrorMessage;
                    break;
                case SensorServiceState.Ready when !AppInfo.IsAdministrator:
                    SensorMessage = "Executando sem administrador: temperaturas e alguns sensores ficam indisponíveis.";
                    break;
                case SensorServiceState.Ready when readingCount > 0 && !SensorPicker.CpuTemperature(_sensors.Read()).HasValue:
                    SensorMessage = "Temperatura da CPU indisponível. Ela exige o driver PawnIO (a instalação automática chega em breve).";
                    break;
                default:
                    SensorMessage = null;
                    break;
            }
        }
    }
}
