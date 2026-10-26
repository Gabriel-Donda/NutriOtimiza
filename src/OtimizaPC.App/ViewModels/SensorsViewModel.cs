using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.Core;
using OtimizaPC.Core.Settings;
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
        public SensorItem(SensorReading reading) { Id = reading.Id; Name = reading.Name; KindName = SensorReading.KindName(reading.Kind); Update(reading); }
        public string Id { get; }
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
        public string Name { get; } public SensorKind Kind { get; } public bool IsGauge => Kind == SensorKind.Temperature || Kind == SensorKind.Load || Kind == SensorKind.Level || Kind == SensorKind.Control; public ObservableCollection<SensorItem> Items { get; }
    }
    public sealed partial class SensorGroup : ObservableObject
    {
        public SensorGroup(string groupId, string name, string hardwareName, HardwareKind kind, bool isExpanded)
        {
            GroupId = groupId;
            Name = name;
            HardwareName = hardwareName;
            Icon = IconFor(kind);
            IsExpanded = isExpanded;
        }
        public string GroupId { get; }
        public string HardwareName { get; }
        public string Name { get; } public SymbolRegular Icon { get; }
        [ObservableProperty] private bool _isExpanded;
        public event EventHandler ExpansionChanged;
        partial void OnIsExpandedChanged(bool value) => ExpansionChanged?.Invoke(this, EventArgs.Empty);
        public ObservableCollection<SensorItem> Items { get; } = new ObservableCollection<SensorItem>();
        public ObservableCollection<SensorSection> Sections { get; } = new ObservableCollection<SensorSection>();
        public string MainValue => Items.FirstOrDefault(i => i.IsGauge)?.Value ?? Items.FirstOrDefault()?.Value ?? "—";
        public string TemperatureMax => Items.FirstOrDefault(i => i.KindName == "Temperatura")?.Max ?? "—";
        public string TemperatureMaximumText => "Máx " + TemperatureMax;
        public bool HasTemperature => Items.Any(i => i.KindName == "Temperatura");
        public string Subtitle => !string.IsNullOrWhiteSpace(HardwareName) && !string.Equals(HardwareName, Name, StringComparison.OrdinalIgnoreCase)
            ? HardwareName
            : Items.Count == 1 ? "1 sensor" : $"{Items.Count} sensores";
        private static SymbolRegular IconFor(HardwareKind kind)
        {
            switch (kind) { case HardwareKind.Cpu: return SymbolRegular.DeveloperBoard24; case HardwareKind.Gpu: return SymbolRegular.Video24; case HardwareKind.Memory: return SymbolRegular.Ram20; case HardwareKind.Motherboard: return SymbolRegular.Board24; case HardwareKind.Storage: return SymbolRegular.Storage24; case HardwareKind.Battery: return SymbolRegular.Battery624; case HardwareKind.Network: return SymbolRegular.NetworkCheck24; default: return SymbolRegular.Temperature24; }
        }
        public void RebuildSections()
        {
            var grouped = Items.GroupBy(i => i.KindName).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var section in Sections.ToList())
            {
                if (!grouped.ContainsKey(section.Name)) { Sections.Remove(section); continue; }
                foreach (var item in section.Items.Where(item => !grouped[section.Name].Contains(item)).ToList()) section.Items.Remove(item);
                foreach (var item in grouped[section.Name]) if (!section.Items.Contains(item)) section.Items.Add(item);
            }
            foreach (var group in grouped)
                if (!Sections.Any(s => s.Name == group.Key)) Sections.Add(new SensorSection(group.Key, ParseKind(group.Key), group.Value));
            OnPropertyChanged(nameof(MainValue)); OnPropertyChanged(nameof(TemperatureMax)); OnPropertyChanged(nameof(TemperatureMaximumText)); OnPropertyChanged(nameof(Subtitle));
        }
        private static SensorKind ParseKind(string name) { foreach (SensorKind kind in Enum.GetValues(typeof(SensorKind))) if (SensorReading.KindName(kind) == name) return kind; return SensorKind.Other; }
    }
    public partial class SensorsViewModel : ObservableObject, IActivatable
    {
        private readonly SensorService _sensors; private readonly SettingsStore _store; private readonly AppSettings _settings; private readonly DispatcherTimer _timer; private readonly Dictionary<string, SensorItem> _items = new Dictionary<string, SensorItem>(); private readonly Dictionary<string, SensorGroup> _groups = new Dictionary<string, SensorGroup>(); private bool _busy; private bool _changingExpansion;
        [ObservableProperty] private string _statusMessage = "Carregando sensores…";
        public SensorsViewModel(SensorService sensors, SettingsStore store)
        {
            _sensors = sensors;
            _store = store;
            _settings = _store.Load();
            if (_settings.CollapsedSensorGroups == null) _settings.CollapsedSensorGroups = new List<string>();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += async (_, __) => await RefreshAsync();
        }
        public ObservableCollection<SensorGroup> Groups { get; } = new ObservableCollection<SensorGroup>();
        public bool HasGroups => Groups.Count > 0;
        public void OnActivated() { _timer.Start(); _ = RefreshAsync(); } public void OnDeactivated() => _timer.Stop();
        [RelayCommand]
        private void ToggleGroup(SensorGroup group)
        {
            if (group != null) group.IsExpanded = !group.IsExpanded;
        }

        [RelayCommand]
        private void CollapseAll() => SetAllGroupsExpanded(false);

        [RelayCommand]
        private void ExpandAll() => SetAllGroupsExpanded(true);
        private async Task RefreshAsync()
        {
            if (_busy) return; _busy = true; try { await _sensors.StartAsync(); var readings = await Task.Run(() => _sensors.Read()); Apply(readings); StatusMessage = BuildStatus(readings.Count); } catch (Exception ex) { StatusMessage = "Falha ao atualizar sensores: " + ex.Message; Log.Warn(StatusMessage); } finally { _busy = false; }
        }
        private void Apply(IReadOnlyList<SensorReading> readings)
        {
            var currentReadings = readings.Where(reading => !string.IsNullOrWhiteSpace(reading.Id) && !string.IsNullOrWhiteSpace(reading.GroupId)).ToList();
            var currentItemIds = new HashSet<string>(currentReadings.Select(reading => reading.Id));
            var currentGroupIds = new HashSet<string>(currentReadings.Select(reading => reading.GroupId));

            foreach (var reading in currentReadings.OrderBy(reading => reading.Kind))
            {
                if (_items.TryGetValue(reading.Id, out var item)) { item.Update(reading); continue; }
                if (!_groups.TryGetValue(reading.GroupId, out var group))
                {
                    group = new SensorGroup(reading.GroupId, reading.GroupName, reading.HardwareName, reading.HardwareKind, !_settings.CollapsedSensorGroups.Contains(reading.GroupId));
                    group.ExpansionChanged += OnGroupExpansionChanged;
                    _groups[reading.GroupId] = group;
                    Groups.Add(group);
                }
                item = new SensorItem(reading);
                _items[reading.Id] = item;
                group.Items.Add(item);
            }

            foreach (var itemId in _items.Keys.Where(itemId => !currentItemIds.Contains(itemId)).ToList())
            {
                var item = _items[itemId];
                foreach (var group in Groups) group.Items.Remove(item);
                _items.Remove(itemId);
            }
            foreach (var groupId in _groups.Keys.Where(groupId => !currentGroupIds.Contains(groupId)).ToList())
            {
                var group = _groups[groupId];
                group.ExpansionChanged -= OnGroupExpansionChanged;
                Groups.Remove(group);
                _groups.Remove(groupId);
            }
            foreach (var group in Groups) group.RebuildSections();
            OnPropertyChanged(nameof(HasGroups));
        }
        private void SetAllGroupsExpanded(bool isExpanded)
        {
            if (Groups.All(group => group.IsExpanded == isExpanded)) return;
            _changingExpansion = true;
            foreach (var group in Groups) group.IsExpanded = isExpanded;
            _changingExpansion = false;
            SaveExpansionState();
        }
        private void OnGroupExpansionChanged(object sender, EventArgs e)
        {
            if (!_changingExpansion) SaveExpansionState();
        }
        private void SaveExpansionState()
        {
            _settings.CollapsedSensorGroups.Clear();
            _settings.CollapsedSensorGroups.AddRange(Groups.Where(group => !group.IsExpanded).Select(group => group.GroupId));
            _store.Save(_settings);
        }
        private string BuildStatus(int count) { var text = $"{count} sensores em {Groups.Count} componentes · atualização a cada 1 s"; return AppInfo.IsAdministrator ? text : text + " · execute como administrador para ver todos"; }
    }
}
