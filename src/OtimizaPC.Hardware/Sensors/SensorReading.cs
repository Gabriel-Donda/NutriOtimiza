using System.Globalization;
using OtimizaPC.Core;

namespace OtimizaPC.Hardware.Sensors
{
    public enum HardwareKind
    {
        Cpu,
        Gpu,
        Memory,
        Motherboard,
        Storage,
        Battery,
        Controller,
        Network,
        Other,
    }

    public enum SensorKind
    {
        Temperature,
        Load,
        Clock,
        Fan,
        Power,
        Voltage,
        Current,
        Level,
        Control,
        Data,
        SmallData,
        Throughput,
        Other,
    }

    /// <summary>Leitura instantânea de um sensor (independente da LibreHardwareMonitor).</summary>
    public sealed class SensorReading
    {
        public string Id { get; set; }

        /// <summary>Nome do componente principal (ex.: "AMD Ryzen 7 5800X").</summary>
        public string HardwareName { get; set; }

        /// <summary>Nome para agrupamento, incluindo sub-componentes (ex.: "Placa-mãe / Nuvoton NCT6798D").</summary>
        public string GroupName { get; set; }

        public string GroupId { get; set; }

        public HardwareKind HardwareKind { get; set; }

        /// <summary>Fabricante da GPU (Nvidia, Amd, Intel) quando aplicável.</summary>
        public string Vendor { get; set; }

        public string Name { get; set; }

        public SensorKind Kind { get; set; }

        public float? Value { get; set; }

        public float? Min { get; set; }

        public float? Max { get; set; }

        public string FormattedValue => FormatValue(Kind, Value);

        public static string FormatValue(SensorKind kind, float? value)
        {
            if (!value.HasValue) return "—";
            var v = value.Value;
            var c = CultureInfo.CurrentCulture;
            switch (kind)
            {
                case SensorKind.Temperature: return v.ToString("N0", c) + " °C";
                case SensorKind.Load:
                case SensorKind.Level:
                case SensorKind.Control: return v.ToString("N0", c) + "%";
                case SensorKind.Clock: return v.ToString("N0", c) + " MHz";
                case SensorKind.Fan: return v.ToString("N0", c) + " RPM";
                case SensorKind.Power: return v.ToString("N1", c) + " W";
                case SensorKind.Voltage: return v.ToString("N3", c) + " V";
                case SensorKind.Current: return v.ToString("N2", c) + " A";
                case SensorKind.Data: return Format.Bytes(v * 1024d * 1024 * 1024);
                case SensorKind.SmallData: return Format.Bytes(v * 1024d * 1024);
                case SensorKind.Throughput: return Format.Rate(v);
                default: return v.ToString("N2", c);
            }
        }

        public static string KindName(SensorKind kind)
        {
            switch (kind)
            {
                case SensorKind.Temperature: return "Temperatura";
                case SensorKind.Load: return "Uso";
                case SensorKind.Clock: return "Clock";
                case SensorKind.Fan: return "Ventoinha";
                case SensorKind.Power: return "Potência";
                case SensorKind.Voltage: return "Tensão";
                case SensorKind.Current: return "Corrente";
                case SensorKind.Level: return "Nível";
                case SensorKind.Control: return "Controle";
                case SensorKind.Data:
                case SensorKind.SmallData: return "Dados";
                case SensorKind.Throughput: return "Taxa";
                default: return "Outro";
            }
        }
    }
}
