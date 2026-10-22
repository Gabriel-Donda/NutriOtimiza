using System;
using System.Collections.Generic;
using System.Linq;

namespace OtimizaPC.Hardware.Sensors
{
    /// <summary>Escolhe os sensores "principais" (temperatura da CPU, uso da GPU etc.) entre todas as leituras.</summary>
    public static class SensorPicker
    {
        private static readonly string[] CpuTemperatureNames =
            { "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Tdie", "Tctl", "Core Average", "Core Max", "CPU Cores" };

        public static float? CpuTemperature(IEnumerable<SensorReading> readings) =>
            Pick(readings.Where(r => r.HardwareKind == HardwareKind.Cpu && r.Kind == SensorKind.Temperature), CpuTemperatureNames);

        public static float? CpuPower(IEnumerable<SensorReading> readings) =>
            Pick(readings.Where(r => r.HardwareKind == HardwareKind.Cpu && r.Kind == SensorKind.Power), "CPU Package", "Package");

        public static float? CpuClock(IEnumerable<SensorReading> readings)
        {
            var clocks = readings
                .Where(r => r.HardwareKind == HardwareKind.Cpu && r.Kind == SensorKind.Clock && r.Value.HasValue
                            && r.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(r => r.Value.Value)
                .ToList();
            return clocks.Count > 0 ? clocks.Max() : (float?)null;
        }

        /// <summary>GPU principal: prefere placa dedicada (Nvidia/AMD) à integrada (Intel).</summary>
        public static string MainGpuName(IEnumerable<SensorReading> readings) =>
            readings
                .Where(r => r.HardwareKind == HardwareKind.Gpu)
                .OrderBy(r => r.Vendor == "Intel" ? 1 : 0)
                .Select(r => r.HardwareName)
                .FirstOrDefault();

        public static float? GpuTemperature(IEnumerable<SensorReading> readings, string gpuName) =>
            Pick(ForGpu(readings, gpuName, SensorKind.Temperature), "GPU Core", "GPU Hot Spot");

        public static float? GpuLoad(IEnumerable<SensorReading> readings, string gpuName) =>
            Pick(ForGpu(readings, gpuName, SensorKind.Load), "GPU Core", "D3D 3D");

        private static IEnumerable<SensorReading> ForGpu(IEnumerable<SensorReading> readings, string gpuName, SensorKind kind) =>
            readings.Where(r => r.HardwareKind == HardwareKind.Gpu && r.HardwareName == gpuName && r.Kind == kind);

        /// <summary>Primeiro sensor com valor cujo nome está na lista de preferência; senão, o primeiro com valor.</summary>
        private static float? Pick(IEnumerable<SensorReading> candidates, params string[] preferred)
        {
            var list = candidates.Where(r => r.Value.HasValue).ToList();
            foreach (var name in preferred)
            {
                var match = list.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.Value;
            }
            return list.FirstOrDefault()?.Value;
        }
    }
}
