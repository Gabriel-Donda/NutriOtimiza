using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OtimizaPC.Core;
using OtimizaPC.Hardware.Sensors;
using Wpf.Ui.Controls;

namespace OtimizaPC.App.ViewModels
{
    public partial class SensorItem : ObservableObject
    {
        [ObservableProperty] private string _value;
        [ObservableProperty] private string _min;
        [ObservableProperty] private string _max;
        [ObservableProperty] private double _gaugeValue;
        [ObservableProperty] private Brush _statusBrush;
        private SensorKind _kind;
        public SensorItem(SensorReading reading) { Name = reading.Name; KindName = SensorReading.KindName(reading.Kind); Update(reading); }
        public string Name { get; }
        public string KindName { get; }
        public bool IsGauge => _kind == SensorKind.Temperature || _kind == SensorKind.Load || _kind == SensorKind.Level || _kind == SensorKind.Control;
        public void Update(SensorReading reading)
        {
            _kind = reading.Kind; Value = reading.FormattedValue; Min = SensorReading.FormatValue(reading.Kind, reading.Min); Max = SensorReading.FormatValue(reading.Kind, reading.Max);
            GaugeValue = reading.Value.HasValue ? Math.Max(0, Math.Min(100, reading.Value.Value)) : 0;
            var warning = reading.Kind == SensorKind.Temperature ? 70 : 70; var critical = reading.Kind == SensorKind.Temperature ? 85 : 90;
            var key = reading.Value >= critical ? "StatusCriticalBrush" : reading.Value >= warning ? "StatusWarningBrush" : "StatusOkBrush";
            StatusBrush = Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray; OnPropertyChanged(nameof(IsGauge));
        }
    }
    public sealed class SensorSection
    {
        public SensorSection(string name, SensorKind kind, IEnumerable<SensorItem> items) { Name = name; Kind = kind; Items = new ObservableCollection<SensorItem>(items); }
        public string Name { get; } public SensorKind Kind { get; } public bool IsGauge => Kind != SensorKind.Other; public ObservableCollection<SensorItem> Items { get; }
    }
    public sealed class SensorGroup
    {
        public SensorGroup(string name, HardwareKind kind) { Name = name; Icon = IconFor(kind); }
        public string Name { get; } public SymbolRegular Icon { get; }
        public ObservableCollection<SensorItem> Items { get; } = new ObservableCollection<SensorItem>();
        public ObservableCollection<SensorSection> Sections { get; } = new ObservableCollection<SensorSection>();
        public string MainValue => Items.FirstOrDefault(i => i.IsGauge)?.Value ?? Items.FirstOrDefault()?.Value ?? "—";
        public string TemperatureMax => Items.FirstOrDefault(i => i.KindName == "Temperatura")?.Max ?? "—";
        public string MainKind => Items.FirstOrDefault(i => i.IsGauge)?.KindName ?? Items.FirstOrDefault()?.KindName ?? "Sensores";
        private static SymbolRegular IconFor(HardwareKind kind)
        {
            switch (kind) { case HardwareKind.Cpu: return SymbolRegular.DeveloperBoard24; case HardwareKind.Gpu: return SymbolRegular.Video24; case HardwareKind.Memory: return SymbolRegular.Ram20; case HardwareKind.Motherboard: return SymbolRegular.Board24; case HardwareKind.Storage: return SymbolRegular.Storage24; case HardwareKind.Battery: return SymbolRegular.Battery624; case HardwareKind.Network: return SymbolRegular.NetworkCheck24; default: return SymbolRegular.Temperature24; }
        }
        public void RebuildSections()
        {
            Sections.Clear(); foreach (var group in Items.GroupBy(i => i.KindName)) Sections.Add(new SensorSection(group.Key, ParseKind(group.Key), group));
            OnPropertyChanged(nameof(MainValue)); OnPropertyChanged(nameof(TemperatureMax)); OnPropertyChanged(nameof(MainKind));
        }
        private static SensorKind ParseKind(string name) { foreach (SensorKind kind in Enum.GetValues(typeof(SensorKind))) if (SensorReading.KindName(kind) == name) return kind; return SensorKind.Other; }
        private void OnPropertyChanged(string name) { }
    }
    public partial class SensorsViewModel : ObservableObject, IActivatable
    {
        private readonly SensorService _sensors; private readonly DispatcherTimer _timer; private readonly Dictionary<string, SensorItem> _items = new Dictionary<string, SensorItem>(); private readonly Dictionary<string, SensorGroup> _groups = new Dictionary<string, SensorGroup>(); private bool _busy;
        [ObservableProperty] private string _statusMessage = "Carregando sensores…";
        public SensorsViewModel(SensorService sensors) { _sensors = sensors; _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; _timer.Tick += async (_, __) => await RefreshAsync(); }
        public ObservableCollection<SensorGroup> Groups { get; } = new ObservableCollection<SensorGroup>(); public void OnActivated() { _timer.Start(); _ = RefreshAsync(); } public void OnDeactivated() => _timer.Stop();
        private async Task RefreshAsync()
        {
            if (_busy) return; _busy = true; try { await _sensors.StartAsync(); var readings = await Task.Run(() => _sensors.Read()); Apply(readings); StatusMessage = BuildStatus(readings.Count); } catch (Exception ex) { Log.Warn("Falha ao atualizar sensores: " + ex.Message); } finally { _busy = false; }
        }
        private void Apply(IReadOnlyList<SensorReading> readings)
        {
            foreach (var reading in readings.OrderBy(r => r.Kind)) { if (_items.TryGetValue(reading.Id, out var item)) { item.Update(reading); continue; } if (!_groups.TryGetValue(reading.GroupId, out var group)) { group = new SensorGroup(reading.GroupName, reading.HardwareKind); _groups[reading.GroupId] = group; Groups.Add(group); } item = new SensorItem(reading); _items[reading.Id] = item; group.Items.Add(item); group.RebuildSections(); }
            foreach (var group in Groups) group.RebuildSections();
        }
        private string BuildStatus(int count) { var text = $"{count} sensores em {Groups.Count} componentes · atualização a cada 1 s"; return AppInfo.IsAdministrator ? text : text + " · execute como administrador para ver todos"; }
    }
}
