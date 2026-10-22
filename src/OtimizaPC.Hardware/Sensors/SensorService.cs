using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using OtimizaPC.Core;

namespace OtimizaPC.Hardware.Sensors
{
    public enum SensorServiceState
    {
        Starting,
        Ready,
        Failed,
    }

    /// <summary>
    /// Encapsula a LibreHardwareMonitor. A biblioteca não é thread-safe, então todo acesso passa por um lock.
    /// Leituras em intervalo menor que 500 ms devolvem o último resultado em cache.
    /// </summary>
    public sealed class SensorService : IDisposable
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMilliseconds(500);

        private readonly object _sync = new object();
        private Computer _computer;
        private bool _disposed;
        private Task _startTask;
        private DateTime _lastRead = DateTime.MinValue;
        private IReadOnlyList<SensorReading> _last = new SensorReading[0];

        public SensorServiceState State { get; private set; } = SensorServiceState.Starting;

        public string ErrorMessage { get; private set; }

        public Task StartAsync()
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SensorService));
                return _startTask ?? (_startTask = Task.Run(Open));
            }
        }

        /// <summary>Reabre os sensores após instalar o driver, sem concorrer com leituras.</summary>
        public async Task RestartAsync()
        {
            await StartAsync().ConfigureAwait(false);
            Task restart;
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SensorService));
                _computer?.Close();
                _computer = null;
                _last = new SensorReading[0];
                _lastRead = DateTime.MinValue;
                ErrorMessage = null;
                State = SensorServiceState.Starting;
                restart = _startTask = Task.Run(Open);
            }
            await restart.ConfigureAwait(false);
        }

        private void Open()
        {
            try
            {
                var computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true,
                    IsStorageEnabled = true,
                    IsBatteryEnabled = true,
                };
                computer.Open();

                lock (_sync)
                {
                    if (_disposed)
                    {
                        computer.Close();
                        return;
                    }
                    _computer = computer;
                    State = SensorServiceState.Ready;
                }
                Log.Info($"Sensores iniciados: {computer.Hardware.Count} componentes detectados.");
            }
            catch (Exception ex)
            {
                State = SensorServiceState.Failed;
                ErrorMessage = ex.Message;
                Log.Error("Falha ao iniciar a leitura de sensores", ex);
            }
        }

        /// <summary>Atualiza e devolve todas as leituras. Seguro para chamar de qualquer thread.</summary>
        public IReadOnlyList<SensorReading> Read()
        {
            lock (_sync)
            {
                if (_computer == null || DateTime.UtcNow - _lastRead < CacheDuration)
                    return _last;

                var readings = new List<SensorReading>();
                foreach (var hardware in _computer.Hardware)
                    Collect(hardware, hardware, readings);

                _last = readings;
                _lastRead = DateTime.UtcNow;
                return readings;
            }
        }

        private static void Collect(IHardware root, IHardware hardware, List<SensorReading> readings)
        {
            try
            {
                hardware.Update();
            }
            catch (Exception ex)
            {
                Log.Warn($"Falha ao atualizar {hardware.Name}: {ex.Message}");
                return;
            }

            var rootType = root.HardwareType.ToString();
            var groupName = ReferenceEquals(root, hardware) ? root.Name : root.Name + " / " + hardware.Name;

            foreach (var sensor in hardware.Sensors)
            {
                readings.Add(new SensorReading
                {
                    Id = sensor.Identifier.ToString(),
                    HardwareName = root.Name,
                    GroupName = groupName,
                    GroupId = hardware.Identifier.ToString(),
                    HardwareKind = MapHardware(rootType),
                    Vendor = GpuVendor(rootType),
                    Name = sensor.Name,
                    Kind = MapSensor(sensor.SensorType.ToString()),
                    Value = sensor.Value,
                    Min = sensor.Min,
                    Max = sensor.Max,
                });
            }

            foreach (var sub in hardware.SubHardware)
                Collect(root, sub, readings);
        }

        // Mapeamento por nome para não quebrar se a biblioteca adicionar novos tipos
        private static HardwareKind MapHardware(string type)
        {
            switch (type)
            {
                case "Cpu": return HardwareKind.Cpu;
                case "GpuNvidia":
                case "GpuAmd":
                case "GpuIntel": return HardwareKind.Gpu;
                case "Memory": return HardwareKind.Memory;
                case "Motherboard":
                case "SuperIO":
                case "EmbeddedController": return HardwareKind.Motherboard;
                case "Storage": return HardwareKind.Storage;
                case "Battery": return HardwareKind.Battery;
                case "Cooler": return HardwareKind.Controller;
                case "Network": return HardwareKind.Network;
                default: return HardwareKind.Other;
            }
        }

        private static string GpuVendor(string type) =>
            type.StartsWith("Gpu", StringComparison.Ordinal) ? type.Substring(3) : null;

        private static SensorKind MapSensor(string type)
        {
            switch (type)
            {
                case "Temperature": return SensorKind.Temperature;
                case "Load": return SensorKind.Load;
                case "Clock": return SensorKind.Clock;
                case "Fan": return SensorKind.Fan;
                case "Power": return SensorKind.Power;
                case "Voltage": return SensorKind.Voltage;
                case "Current": return SensorKind.Current;
                case "Level": return SensorKind.Level;
                case "Control": return SensorKind.Control;
                case "Data": return SensorKind.Data;
                case "SmallData": return SensorKind.SmallData;
                case "Throughput": return SensorKind.Throughput;
                default: return SensorKind.Other;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                try
                {
                    _computer?.Close();
                }
                catch (Exception ex)
                {
                    Log.Warn("Falha ao fechar sensores: " + ex.Message);
                }
                _computer = null;
            }
        }
    }
}
