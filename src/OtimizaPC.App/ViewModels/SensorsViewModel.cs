using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OtimizaPC.Core;
using OtimizaPC.Hardware.Sensors;
using Wpf.Ui.Controls;

namespace OtimizaPC.App.ViewModels
{
    public partial class SensorItem : ObservableObject
    {
        [ObservableProperty]
        private string _value;

        [ObservableProperty]
        private string _min;

        [ObservableProperty]
        private string _max;

        public SensorItem(SensorReading reading)
        {
            Name = reading.Name;
            KindName = SensorReading.KindName(reading.Kind);
            Update(reading);
        }

        public string Name { get; }

        public string KindName { get; }

        public void Update(SensorReading reading)
        {
            Value = reading.FormattedValue;
            Min = SensorReading.FormatValue(reading.Kind, reading.Min);
            Max = SensorReading.FormatValue(reading.Kind, reading.Max);
        }
    }

    public sealed class SensorGroup
    {
        public SensorGroup(string name, HardwareKind kind)
        {
            Name = name;
            Icon = IconFor(kind);
        }

        public string Name { get; }

        public SymbolRegular Icon { get; }

        public ObservableCollection<SensorItem> Items { get; } = new ObservableCollection<SensorItem>();

        private static SymbolRegular IconFor(HardwareKind kind)
        {
            switch (kind)
            {
                case HardwareKind.Cpu: return SymbolRegular.DeveloperBoard24;
                case HardwareKind.Gpu: return SymbolRegular.Video24;
                case HardwareKind.Memory: return SymbolRegular.Ram20;
                case HardwareKind.Motherboard: return SymbolRegular.Board24;
                case HardwareKind.Storage: return SymbolRegular.Storage24;
                case HardwareKind.Battery: return SymbolRegular.Battery624;
                case HardwareKind.Network: return SymbolRegular.NetworkCheck24;
                default: return SymbolRegular.Temperature24;
            }
        }
    }

    /// <summary>Lista ao vivo de todos os sensores (atual / mínimo / máximo), estilo HWMonitor.</summary>
    public partial class SensorsViewModel : ObservableObject, IActivatable
    {
        private readonly SensorService _sensors;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, SensorItem> _items = new Dictionary<string, SensorItem>();
        private readonly Dictionary<string, SensorGroup> _groups = new Dictionary<string, SensorGroup>();
        private bool _busy;

        [ObservableProperty]
        private string _statusMessage = "Carregando sensores…";

        public SensorsViewModel(SensorService sensors)
        {
            _sensors = sensors;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += async (_, __) => await RefreshAsync();
        }

        public ObservableCollection<SensorGroup> Groups { get; } = new ObservableCollection<SensorGroup>();

        public void OnActivated()
        {
            _timer.Start();
            _ = RefreshAsync();
        }

        public void OnDeactivated() => _timer.Stop();

        private async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await _sensors.StartAsync();
                var readings = await Task.Run(() => _sensors.Read());
                Apply(readings);
                StatusMessage = BuildStatus(readings.Count);
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao atualizar sensores: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Atualiza os itens existentes no lugar (sem recriar a lista, evitando piscar).</summary>
        private void Apply(IReadOnlyList<SensorReading> readings)
        {
            // OrderBy é estável: mantém a ordem da biblioteca dentro de cada tipo
            foreach (var reading in readings.OrderBy(r => r.Kind))
            {
                if (_items.TryGetValue(reading.Id, out var item))
                {
                    item.Update(reading);
                    continue;
                }

                if (!_groups.TryGetValue(reading.GroupId, out var group))
                {
                    group = new SensorGroup(reading.GroupName, reading.HardwareKind);
                    _groups[reading.GroupId] = group;
                    Groups.Add(group);
                }

                item = new SensorItem(reading);
                _items[reading.Id] = item;
                group.Items.Add(item);
            }
        }

        private string BuildStatus(int count)
        {
            switch (_sensors.State)
            {
                case SensorServiceState.Failed: return "Falha ao iniciar sensores: " + _sensors.ErrorMessage;
                case SensorServiceState.Starting: return "Carregando sensores…";
            }

            var text = $"{count} sensores em {Groups.Count} componentes · atualização a cada 1 s";
            return AppInfo.IsAdministrator ? text : text + " · execute como administrador para ver todos";
        }
    }
}
