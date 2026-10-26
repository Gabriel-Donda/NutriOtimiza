using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Win32;
using OtimizaPC.Core;
using OtimizaPC.Core.Models;
using OtimizaPC.Hardware.Interop;
using OtimizaPC.Hardware.Sensors;

namespace OtimizaPC.Hardware
{
    /// <summary>Coleta informações detalhadas de hardware e sistema via WMI, registro e sensores.</summary>
    public sealed class HardwareInfoService
    {
        private readonly SensorService _sensors;

        public HardwareInfoService(SensorService sensors)
        {
            _sensors = sensors;
        }

        public Task<HardwareReport> CollectAsync() => Task.Run(() => Collect());

        public Task<List<InfoEntry>> GetSummaryAsync() => Task.Run(() => GetSummary());

        private HardwareReport Collect()
        {
            var report = new HardwareReport { GeneratedAt = DateTime.Now, MachineName = Environment.MachineName };
            var readings = _sensors.Read();

            var builders = new (string Title, string Icon, Action<InfoSection> Fill)[]
            {
                ("Sistema", "Desktop24", FillSystem),
                ("Processador", "DeveloperBoard24", s => FillCpu(s, readings)),
                ("Placa-mãe e BIOS", "Board24", FillMotherboard),
                ("Memória RAM", "Ram20", FillMemory),
                ("Placa de vídeo e monitores", "Video24", s => FillGpu(s, readings)),
                ("Armazenamento", "Storage24", s => FillStorage(s, readings)),
                ("Rede", "NetworkCheck24", FillNetwork),
                ("Bateria", "Battery624", FillBattery),
            };

            // Seções em paralelo: o total passa a ser o tempo da mais lenta (ativação do Windows), não a soma
            var sections = builders
                .AsParallel()
                .AsOrdered()
                .WithDegreeOfParallelism(builders.Length)
                .Select(b => BuildSection(b.Title, b.Icon, b.Fill))
                .Where(s => s != null)
                .ToList();

            report.Sections.AddRange(sections);
            return report;
        }

        /// <summary>Monta a seção; devolve null quando não há nada a exibir (ex.: desktop sem bateria).</summary>
        private static InfoSection BuildSection(string title, string icon, Action<InfoSection> fill)
        {
            var section = new InfoSection(title, icon);
            var watch = Stopwatch.StartNew();
            try
            {
                fill(section);
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao coletar a seção '{title}'", ex);
                section.NewGroup().Add("Erro", "Não foi possível coletar: " + ex.Message);
            }
            Log.Info($"Seção '{title}' coletada em {watch.ElapsedMilliseconds} ms");

            return section.Groups.Any(g => g.Entries.Count > 0) ? section : null;
        }

        #region Resumo (painel)

        private static List<InfoEntry> GetSummary()
        {
            var cs = Wmi.First("SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem");
            var os = Wmi.First("SELECT Caption, LastBootUpTime FROM Win32_OperatingSystem");
            var bios = Wmi.First("SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS");
            var board = Wmi.First("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            var cpus = Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            var sticks = Wmi.Query("SELECT Capacity FROM Win32_PhysicalMemory");
            var arrays = Wmi.Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
            var video = Wmi.Query("SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");

            var installed = sticks.Sum(s => (double)(s.ULong("Capacity") ?? 0));
            var memory = SystemMetrics.GetMemory();

            var data = new SummaryData
            {
                Manufacturer = cs.Str("Manufacturer"),
                Model = cs.Str("Model"),
                OsCaption = os.Str("Caption"),
                OsVersion = WindowsInfo.DisplayVersion,
                BiosVendor = bios.Str("Manufacturer"),
                BiosVersion = bios.Str("SMBIOSBIOSVersion"),
                BoardManufacturer = board.Str("Manufacturer"),
                BoardProduct = board.Str("Product"),
                CpuName = cpus.Select(c => c.Str("Name")).FirstOrDefault(n => n != null),
                Cores = SumInt(cpus, "NumberOfCores"),
                Threads = SumInt(cpus, "NumberOfLogicalProcessors"),
                MemoryTotal = installed > 0
                    ? (ulong)Math.Round(installed)
                    : memory.Total > 0 ? (ulong?)memory.Total : null,
                MemorySticks = sticks.Count,
                MemorySlots = SumInt(arrays, "MemoryDevices"),
                Gpu = string.Join(", ", video.Select(v => v.Str("Name")).Where(n => n != null).Distinct()),
                Disks = SummaryDisks(),
                Network = GetPrimaryIPv4(),
                Resolution = SummaryResolution(video),
                Battery = SummaryBattery(cs.Int("PCSystemType") == 2),
                LastBoot = os.Date("LastBootUpTime"),
            };

            return DashboardSummary.Build(data);
        }

        private static int? SumInt(IEnumerable<WmiRow> rows, string property)
        {
            var values = rows.Select(r => r.Int(property)).Where(v => v.HasValue).ToList();
            return values.Count == 0 ? (int?)null : values.Sum(v => v.Value);
        }

        private static string SummaryDisks()
        {
            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).ToList();
                if (drives.Count == 0) return null;
                return string.Join(" · ", drives.Select(d =>
                    $"{d.Name.TrimEnd('\\')} {Format.Bytes(d.TotalFreeSpace)} livres de {Format.Bytes(d.TotalSize)}"));
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao resumir os discos: " + ex.Message);
                return null;
            }
        }

        private static string GetPrimaryIPv4()
        {
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

                foreach (var nic in nics)
                {
                    try
                    {
                        var address = nic.GetIPProperties().UnicastAddresses
                            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                        if (address != null) return address.Address.ToString();
                    }
                    catch
                    {
                        // Adaptador virtual sem propriedades de IP acessíveis
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao obter o IP principal: " + ex.Message);
            }
            return null;
        }

        private static string SummaryResolution(IEnumerable<WmiRow> video)
        {
            foreach (var v in video)
            {
                var width = v.Int("CurrentHorizontalResolution");
                var height = v.Int("CurrentVerticalResolution");
                if (width > 0 && height > 0) return $"{width} × {height}";
            }
            return null;
        }

        private static string SummaryBattery(bool isMobile)
        {
            var batteries = Wmi.Query("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
            if (batteries.Count == 0) return isMobile ? null : "Não possui bateria";

            var battery = batteries[0];
            var charge = battery.Int("EstimatedChargeRemaining");
            var status = BatteryStatus(battery.Int("BatteryStatus"));
            if (charge.HasValue && status != null) return $"{charge}% ({status})";
            if (charge.HasValue) return $"{charge}%";
            return status;
        }

        #endregion

        #region Sistema

        private static void FillSystem(InfoSection s)
        {
            var os = Wmi.First("SELECT * FROM Win32_OperatingSystem");
            var cs = Wmi.First("SELECT * FROM Win32_ComputerSystem");

            var g = s.NewGroup();
            g.Add("Nome do computador", Environment.MachineName);
            g.Add("Fabricante / modelo", Format.Join(" ", cs.Str("Manufacturer"), cs.Str("Model")));
            g.Add(cs.Bool("PartOfDomain") == true ? "Domínio" : "Grupo de trabalho", cs.Str("Domain") ?? cs.Str("Workgroup"));
            g.Add("Usuário", Environment.UserDomainName + "\\" + Environment.UserName);
            g.Add("Sistema operacional", os.Str("Caption"));
            g.Add("Versão", WindowsInfo.DisplayVersion);
            g.Add("Arquitetura", os.Str("OSArchitecture"));
            g.Add("Instalado em", Format.Date(os.Date("InstallDate")));

            var boot = os.Date("LastBootUpTime");
            if (boot.HasValue)
            {
                g.Add("Último boot", Format.Date(boot, withTime: true));
                g.Add("Tempo ligado", Format.Duration(DateTime.Now - boot.Value));
            }
            g.Add("Ativação do Windows", GetActivation());

            var fw = s.NewGroup("Firmware e segurança");
            fw.Add("Modo de boot", WindowsInfo.FirmwareType);
            fw.Add("Secure Boot", WindowsInfo.SecureBoot);
            fw.Add("TPM", GetTpm());
        }

        private static string GetActivation()
        {
            // ApplicationID do Windows; a consulta é lenta (alguns segundos), por isso só roda no relatório completo
            var row = Wmi.First(
                "SELECT LicenseStatus, Description FROM SoftwareLicensingProduct " +
                "WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL");
            if (row.IsEmpty) return null;

            string status;
            switch (row.Int("LicenseStatus"))
            {
                case 1: status = "Ativado"; break;
                case 2: status = "Período de carência inicial"; break;
                case 3: status = "Período de carência (hardware alterado)"; break;
                case 4: status = "Não genuíno"; break;
                case 5: status = "Modo de notificação (não ativado)"; break;
                case 6: status = "Carência estendida"; break;
                default: status = "Não licenciado"; break;
            }

            var description = row.Str("Description") ?? string.Empty;
            string channel = null;
            if (description.IndexOf("RETAIL", StringComparison.OrdinalIgnoreCase) >= 0) channel = "Varejo";
            else if (description.IndexOf("OEM", StringComparison.OrdinalIgnoreCase) >= 0) channel = "OEM";
            else if (description.IndexOf("KMS", StringComparison.OrdinalIgnoreCase) >= 0) channel = "Volume (KMS)";
            else if (description.IndexOf("MAK", StringComparison.OrdinalIgnoreCase) >= 0) channel = "Volume (MAK)";

            return channel == null ? status : $"{status} — licença {channel}";
        }

        private static string GetTpm()
        {
            var tpm = Wmi.First("SELECT * FROM Win32_Tpm", Wmi.Tpm);
            if (tpm.IsEmpty)
                return AppInfo.IsAdministrator ? "Não encontrado" : "Requer administrador para verificar";

            var version = tpm.Str("SpecVersion")?.Split(',')[0].Trim();
            var enabled = tpm.Bool("IsEnabled_InitialValue") == true ? "habilitado" : "desabilitado";
            return version == null ? $"Presente ({enabled})" : $"Versão {version} ({enabled})";
        }

        #endregion

        #region Processador

        private static void FillCpu(InfoSection s, IReadOnlyList<SensorReading> readings)
        {
            var cpus = Wmi.Query("SELECT * FROM Win32_Processor");
            var hypervisor = Wmi.First("SELECT * FROM Win32_ComputerSystem").Bool("HypervisorPresent") == true;

            for (var i = 0; i < cpus.Count; i++)
            {
                var p = cpus[i];
                var g = s.NewGroup(cpus.Count > 1 ? $"Processador {i + 1}" : null);
                g.Add("Modelo", p.Str("Name"));
                g.Add("Fabricante", p.Str("Manufacturer"));
                g.Add("Núcleos / threads", $"{p.Int("NumberOfCores")} núcleos / {p.Int("NumberOfLogicalProcessors")} threads");

                var mhz = p.ULong("MaxClockSpeed");
                if (mhz > 0) g.Add("Clock base", Format.Frequency(mhz.Value));

                var l2 = p.ULong("L2CacheSize");
                var l3 = p.ULong("L3CacheSize");
                if (l2 > 0) g.Add("Cache L2", Format.Bytes(l2.Value * 1024d));
                if (l3 > 0) g.Add("Cache L3", Format.Bytes(l3.Value * 1024d));

                g.Add("Soquete", p.Str("SocketDesignation"));
                g.Add("Arquitetura", CpuArchitecture(p.Int("Architecture")));
                g.Add("Virtualização", hypervisor
                    ? "Ativa (hypervisor em execução)"
                    : p.Bool("VirtualizationFirmwareEnabled") switch
                    {
                        true => "Habilitada no firmware",
                        false => "Desabilitada no firmware",
                        _ => null,
                    });
                g.Add("Identificação", p.Str("Description"));
            }

            var now = s.NewGroup("Leitura atual");
            var temperature = SensorPicker.CpuTemperature(readings);
            var clock = SensorPicker.CpuClock(readings);
            var power = SensorPicker.CpuPower(readings);
            if (temperature.HasValue) now.Add("Temperatura", SensorReading.FormatValue(SensorKind.Temperature, temperature));
            if (clock.HasValue) now.Add("Clock atual (maior núcleo)", Format.Frequency(clock.Value));
            if (power.HasValue) now.Add("Consumo", SensorReading.FormatValue(SensorKind.Power, power));
        }

        private static string CpuArchitecture(int? code)
        {
            switch (code)
            {
                case 0: return "x86";
                case 5: return "ARM";
                case 9: return "x64";
                case 12: return "ARM64";
                default: return null;
            }
        }

        #endregion

        #region Placa-mãe

        private static void FillMotherboard(InfoSection s)
        {
            var board = Wmi.First("SELECT * FROM Win32_BaseBoard");
            var bios = Wmi.First("SELECT * FROM Win32_BIOS");

            var g = s.NewGroup("Placa-mãe");
            g.Add("Fabricante", board.Str("Manufacturer"));
            g.Add("Modelo", board.Str("Product"));
            g.Add("Revisão", board.Str("Version"));
            g.Add("Número de série", board.Str("SerialNumber"));

            var b = s.NewGroup("BIOS / UEFI");
            b.Add("Fabricante", bios.Str("Manufacturer"));
            b.Add("Versão", bios.Str("SMBIOSBIOSVersion"));
            b.Add("Data", Format.Date(bios.Date("ReleaseDate")));
            b.Add("Número de série / Service Tag", bios.Str("SerialNumber"));
            b.Add("Modo de boot", WindowsInfo.FirmwareType);
        }

        #endregion

        #region Memória

        private static void FillMemory(InfoSection s)
        {
            var sticks = Wmi.Query("SELECT * FROM Win32_PhysicalMemory");
            var arrays = Wmi.Query("SELECT * FROM Win32_PhysicalMemoryArray");
            var memory = SystemMetrics.GetMemory();

            var installed = sticks.Sum(m => (double)(m.ULong("Capacity") ?? 0));
            var slots = arrays.Sum(a => a.Int("MemoryDevices") ?? 0);
            var maxKb = arrays.Sum(a => (double)(a.ULong("MaxCapacityEx") ?? a.ULong("MaxCapacity") ?? 0));

            var g = s.NewGroup();
            g.Add("Total instalado", Format.Bytes(installed > 0 ? installed : memory.Total));
            g.Add("Disponível para o sistema", Format.Bytes(memory.Total));
            g.Add("Em uso agora", $"{Format.Bytes(memory.Used)} ({Format.Percent(memory.Percent)})");
            if (slots > 0)
            {
                var free = slots - sticks.Count;
                g.Add("Slots", $"{sticks.Count} de {slots} ocupados" + (free > 0 ? $" — {free} livre(s)" : string.Empty));
            }
            if (maxKb > 0) g.Add("Capacidade máxima suportada", Format.Bytes(maxKb * 1024));
            g.Add("Tipo", string.Join(", ", sticks.Select(MemoryType).Where(t => t != null).Distinct()));

            foreach (var m in sticks)
            {
                var mg = s.NewGroup("Pente: " + Format.Join(" — ", m.Str("DeviceLocator"), m.Str("BankLabel")));
                mg.Add("Capacidade", Format.Bytes(m.ULong("Capacity") ?? 0));
                mg.Add("Tipo", Format.Join(" ", MemoryType(m), FormFactor(m.Int("FormFactor"))));

                var speed = m.Int("Speed");
                var configured = m.Int("ConfiguredClockSpeed");
                if (speed > 0)
                {
                    mg.Add("Velocidade", configured > 0 && configured != speed
                        ? $"{speed} MT/s (operando a {configured} MT/s)"
                        : $"{speed} MT/s");
                }
                mg.Add("Fabricante", MemoryManufacturer(m.Str("Manufacturer")));
                mg.Add("Part number", m.Str("PartNumber"));
                mg.Add("Número de série", m.Str("SerialNumber"));
            }
        }

        private static string MemoryType(WmiRow m)
        {
            // SMBIOSMemoryType existe a partir do Windows 10; MemoryType é o campo antigo
            switch (m.Int("SMBIOSMemoryType") ?? m.Int("MemoryType"))
            {
                case 20: return "DDR";
                case 21: return "DDR2";
                case 22: return "DDR2 FB-DIMM";
                case 24: return "DDR3";
                case 26: return "DDR4";
                case 27: return "LPDDR";
                case 28: return "LPDDR2";
                case 29: return "LPDDR3";
                case 30: return "LPDDR4";
                case 34: return "DDR5";
                case 35: return "LPDDR5";
                default: return null;
            }
        }

        private static string FormFactor(int? code)
        {
            switch (code)
            {
                case 8: return "DIMM";
                case 12: return "SO-DIMM";
                default: return null;
            }
        }

        /// <summary>Alguns BIOS informam o código JEDEC em vez do nome do fabricante.</summary>
        private static string MemoryManufacturer(string raw)
        {
            if (raw == null) return null;
            switch (raw.ToUpperInvariant())
            {
                case "80CE":
                case "CE00": return "Samsung";
                case "80AD":
                case "AD00": return "SK Hynix";
                case "802C":
                case "2C00": return "Micron / Crucial";
                case "0198":
                case "9801": return "Kingston";
                case "04CD":
                case "CD04": return "G.Skill";
                case "029E":
                case "9E02": return "Corsair";
                case "859B":
                case "9B85": return "Crucial";
                case "0443":
                case "4304": return "ADATA";
                default: return raw;
            }
        }

        #endregion

        #region Vídeo

        private static void FillGpu(InfoSection s, IReadOnlyList<SensorReading> readings)
        {
            foreach (var v in Wmi.Query("SELECT * FROM Win32_VideoController"))
            {
                var name = v.Str("Name");
                var g = s.NewGroup(name);

                var vram = GetVideoMemory(name) ?? v.ULong("AdapterRAM");
                if (vram > 0) g.Add("Memória de vídeo", Format.Bytes(vram.Value));
                g.Add("Processador de vídeo", v.Str("VideoProcessor"));
                g.Add("Versão do driver", v.Str("DriverVersion"));
                g.Add("Data do driver", Format.Date(v.Date("DriverDate")));

                var width = v.Int("CurrentHorizontalResolution");
                var height = v.Int("CurrentVerticalResolution");
                var hz = v.Int("CurrentRefreshRate");
                if (width > 0 && height > 0)
                    g.Add("Resolução atual", hz > 1 ? $"{width} × {height} @ {hz} Hz" : $"{width} × {height}");

                var temperature = SensorPicker.GpuTemperature(readings, MatchSensorHardware(readings, HardwareKind.Gpu, name));
                if (temperature.HasValue) g.Add("Temperatura", SensorReading.FormatValue(SensorKind.Temperature, temperature));
            }

            FillMonitors(s);
        }

        /// <summary>A WMI limita AdapterRAM a 4 GB; o valor real fica no registro do driver.</summary>
        private static ulong? GetVideoMemory(string adapterName)
        {
            const string classKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using (var root = Reg.Open(RegistryHive.LocalMachine, classKey))
            {
                if (root == null) return null;
                foreach (var subName in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
                {
                    try
                    {
                        using (var key = root.OpenSubKey(subName))
                        {
                            if (key == null || !string.Equals(key.GetValue("DriverDesc") as string, adapterName, StringComparison.OrdinalIgnoreCase))
                                continue;
                            var size = Reg.ToULong(key.GetValue("HardwareInformation.qwMemorySize"))
                                       ?? Reg.ToULong(key.GetValue("HardwareInformation.MemorySize"));
                            if (size > 0) return size;
                        }
                    }
                    catch
                    {
                        // Subchaves protegidas ("Properties") são ignoradas
                    }
                }
            }
            return null;
        }

        private static void FillMonitors(InfoSection s)
        {
            var sizes = Wmi.Query("SELECT * FROM WmiMonitorBasicDisplayParams", Wmi.RootWmi)
                .Where(r => r.Str("InstanceName") != null)
                .GroupBy(r => r.Str("InstanceName"))
                .ToDictionary(gr => gr.Key, gr => gr.First());

            var index = 0;
            foreach (var m in Wmi.Query("SELECT * FROM WmiMonitorID", Wmi.RootWmi))
            {
                index++;
                var name = DecodeWmiString(m["UserFriendlyName"]);
                var g = s.NewGroup("Monitor " + index + (name != null ? ": " + name : string.Empty));
                g.Add("Fabricante (código)", DecodeWmiString(m["ManufacturerName"]));
                g.Add("Número de série", DecodeWmiString(m["SerialNumberID"]));
                var year = m.Int("YearOfManufacture");
                if (year > 1990) g.Add("Ano de fabricação", year.ToString());

                if (m.Str("InstanceName") is string instance && sizes.TryGetValue(instance, out var basic))
                {
                    var w = basic.Int("MaxHorizontalImageSize") ?? 0;
                    var h = basic.Int("MaxVerticalImageSize") ?? 0;
                    if (w > 0 && h > 0)
                        g.Add("Tamanho aproximado", $"{Math.Sqrt(w * w + h * h) / 2.54:N1}\"");
                }
            }
        }

        private static string DecodeWmiString(object value)
        {
            if (!(value is Array array)) return null;
            var chars = array.Cast<object>().Select(Convert.ToInt32).TakeWhile(c => c != 0).Select(c => (char)c).ToArray();
            var text = new string(chars).Trim();
            return text.Length == 0 ? null : text;
        }

        #endregion

        #region Armazenamento

        private static void FillStorage(InfoSection s, IReadOnlyList<SensorReading> readings)
        {
            // MSFT_PhysicalDisk existe a partir do Windows 8 / Server 2012
            var disks = Wmi.Query("SELECT * FROM MSFT_PhysicalDisk", Wmi.Storage);
            if (disks.Count > 0)
            {
                foreach (var d in disks.OrderBy(d => d.Int("DeviceId") ?? 0))
                {
                    var name = d.Str("FriendlyName") ?? d.Str("Model");
                    var g = s.NewGroup(name);
                    g.Add("Tipo", DiskType(d.Int("MediaType"), d.Int("BusType")));
                    g.Add("Capacidade", Format.Bytes(d.ULong("Size") ?? 0));
                    g.Add("Saúde (Windows)", DiskHealth(d.Int("HealthStatus")));

                    var spindle = d.ULong("SpindleSpeed");
                    if (spindle > 0 && spindle < uint.MaxValue) g.Add("Rotação", $"{spindle} RPM");
                    g.Add("Firmware", d.Str("FirmwareVersion"));
                    g.Add("Número de série", d.Str("SerialNumber"));
                    AddSmart(g, readings, name);
                }
            }
            else
            {
                foreach (var d in Wmi.Query("SELECT * FROM Win32_DiskDrive"))
                {
                    var name = d.Str("Model");
                    var g = s.NewGroup(name);
                    g.Add("Interface", d.Str("InterfaceType"));
                    g.Add("Capacidade", Format.Bytes(d.ULong("Size") ?? 0));
                    g.Add("Status", d.Str("Status"));
                    g.Add("Firmware", d.Str("FirmwareRevision"));
                    g.Add("Número de série", d.Str("SerialNumber"));
                    AddSmart(g, readings, name);
                }
            }

            var volumes = s.NewGroup("Volumes");
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                var label = string.IsNullOrEmpty(drive.VolumeLabel) ? drive.Name : $"{drive.Name} ({drive.VolumeLabel})";
                var freePercent = drive.TotalSize > 0 ? drive.TotalFreeSpace * 100.0 / drive.TotalSize : 0;
                volumes.Add(label, $"{Format.Bytes(drive.TotalFreeSpace)} livres de {Format.Bytes(drive.TotalSize)} ({Format.Percent(freePercent)}) — {drive.DriveFormat}");
            }
        }

        /// <summary>Acrescenta dados SMART lidos pela LibreHardwareMonitor (temperatura, vida útil, TBW).</summary>
        private static void AddSmart(InfoGroup g, IReadOnlyList<SensorReading> readings, string diskName)
        {
            var hardware = MatchSensorHardware(readings, HardwareKind.Storage, diskName);
            if (hardware == null) return;

            foreach (var r in readings.Where(r => r.HardwareKind == HardwareKind.Storage && r.HardwareName == hardware && r.Value.HasValue))
            {
                switch (r.Name)
                {
                    case "Temperature":
                    case "Composite Temperature":
                        g.Add("Temperatura", r.FormattedValue);
                        break;
                    case "Remaining Life":
                        g.Add("Vida útil restante", r.FormattedValue);
                        break;
                    case "Percentage Used":
                        g.Add("Desgaste (NVMe)", r.FormattedValue);
                        break;
                    case "Available Spare":
                        g.Add("Reserva disponível", r.FormattedValue);
                        break;
                    case "Data Written":
                    case "Total Bytes Written":
                    case "Total Host Writes":
                        g.Add("Total gravado", r.FormattedValue);
                        break;
                    case "Data Read":
                    case "Total Host Reads":
                        g.Add("Total lido", r.FormattedValue);
                        break;
                }
            }
        }

        private static string DiskType(int? mediaType, int? busType)
        {
            var media = mediaType == 3 ? "HDD" : mediaType == 4 ? "SSD" : mediaType == 5 ? "SCM" : null;
            string bus;
            switch (busType)
            {
                case 17: bus = "NVMe"; break;
                case 11: bus = "SATA"; break;
                case 7: bus = "USB"; break;
                case 3: bus = "ATA"; break;
                case 10: bus = "SAS"; break;
                case 8: bus = "RAID"; break;
                case 1: bus = "SCSI"; break;
                case 12: bus = "Cartão SD"; break;
                case 16: bus = "Storage Spaces"; break;
                case 15: bus = "Virtual"; break;
                default: bus = null; break;
            }
            return Format.Join(" ", media, bus);
        }

        private static string DiskHealth(int? status)
        {
            switch (status)
            {
                case 0: return "Saudável";
                case 1: return "Atenção";
                case 2: return "Com falha";
                default: return "Desconhecida";
            }
        }

        #endregion

        #region Rede

        private static void FillNetwork(InfoSection s)
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Where(n => n.OperationalStatus == OperationalStatus.Up || !IsVirtualAdapter(n.Description))
                .OrderByDescending(n => n.OperationalStatus == OperationalStatus.Up);

            foreach (var nic in nics)
            {
                var up = nic.OperationalStatus == OperationalStatus.Up;
                var g = s.NewGroup(nic.Name);
                g.Add("Adaptador", nic.Description);
                g.Add("Tipo", NetworkType(nic.NetworkInterfaceType));
                g.Add("Status", up ? "Conectado" : "Desconectado");
                if (up && nic.Speed > 0) g.Add("Velocidade do link", Format.LinkSpeed(nic.Speed));

                var mac = nic.GetPhysicalAddress().GetAddressBytes();
                if (mac.Length > 0) g.Add("Endereço MAC", string.Join("-", mac.Select(b => b.ToString("X2"))));

                if (!up) continue;
                try
                {
                    var props = nic.GetIPProperties();
                    var ipv4 = props.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(a => a.IPv4Mask != null ? $"{a.Address} / {a.IPv4Mask}" : a.Address.ToString());
                    g.Add("IPv4", string.Join(", ", ipv4));
                    g.Add("Gateway", string.Join(", ", props.GatewayAddresses.Select(a => a.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork)));
                    g.Add("DNS", string.Join(", ", props.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork)));
                    g.Add("DHCP", props.GetIPv4Properties()?.IsDhcpEnabled == true ? "Sim" : "Não (IP fixo)");
                }
                catch (Exception ex)
                {
                    Log.Warn($"Falha ao ler IP de {nic.Name}: {ex.Message}");
                }
            }
        }

        /// <summary>Adaptadores virtuais criados pelo Windows que só poluem a lista quando desconectados.</summary>
        private static bool IsVirtualAdapter(string description)
        {
            if (description == null) return false;
            string[] markers = { "Wi-Fi Direct", "Bluetooth", "Virtual Adapter", "WAN Miniport", "Kernel Debug" };
            return markers.Any(m => description.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string NetworkType(NetworkInterfaceType type)
        {
            switch (type)
            {
                case NetworkInterfaceType.Ethernet:
                case NetworkInterfaceType.GigabitEthernet:
                case NetworkInterfaceType.FastEthernetT:
                case NetworkInterfaceType.FastEthernetFx: return "Ethernet (cabo)";
                case NetworkInterfaceType.Wireless80211: return "Wi-Fi";
                case NetworkInterfaceType.Ppp: return "PPP / VPN";
                case NetworkInterfaceType.Wwanpp:
                case NetworkInterfaceType.Wwanpp2: return "Banda larga móvel";
                default: return type.ToString();
            }
        }

        #endregion

        #region Bateria

        private static void FillBattery(InfoSection s)
        {
            var batteries = Wmi.Query("SELECT * FROM Win32_Battery");
            if (batteries.Count == 0) return;

            var statics = Wmi.Query("SELECT * FROM BatteryStaticData", Wmi.RootWmi);
            var fulls = Wmi.Query("SELECT * FROM BatteryFullChargedCapacity", Wmi.RootWmi);
            var cycles = Wmi.Query("SELECT * FROM BatteryCycleCount", Wmi.RootWmi);

            for (var i = 0; i < batteries.Count; i++)
            {
                var b = batteries[i];
                var g = s.NewGroup(batteries.Count > 1 ? $"Bateria {i + 1}" : b.Str("Name"));
                var charge = b.Int("EstimatedChargeRemaining");
                if (charge.HasValue) g.Add("Carga atual", $"{charge}%");
                g.Add("Estado", BatteryStatus(b.Int("BatteryStatus")));

                var st = i < statics.Count ? statics[i] : WmiRow.Empty;
                g.Add("Fabricante", st.Str("ManufactureName"));
                g.Add("Modelo", st.Str("DeviceName"));

                var designed = st.ULong("DesignedCapacity") ?? 0;
                var full = (i < fulls.Count ? fulls[i] : WmiRow.Empty).ULong("FullChargedCapacity") ?? 0;
                if (designed > 0) g.Add("Capacidade de projeto", $"{designed:N0} mWh");
                if (full > 0) g.Add("Capacidade atual (carga completa)", $"{full:N0} mWh");
                if (designed > 0 && full > 0)
                {
                    var health = Math.Min(100, full * 100.0 / designed);
                    g.Add("Saúde da bateria", $"{Format.Percent(health)} (desgaste de {Format.Percent(100 - health)})");
                }

                var cycleCount = (i < cycles.Count ? cycles[i] : WmiRow.Empty).Int("CycleCount");
                if (cycleCount > 0) g.Add("Ciclos de carga", cycleCount.ToString());
            }
        }

        private static string BatteryStatus(int? status)
        {
            switch (status)
            {
                case 1: return "Descarregando";
                case 2: return "Na tomada";
                case 3: return "Carga completa";
                case 4: return "Carga baixa";
                case 5: return "Carga crítica";
                case 6:
                case 7:
                case 8:
                case 9: return "Carregando";
                default: return null;
            }
        }

        #endregion

        /// <summary>Encontra o componente da LibreHardwareMonitor cujo nome corresponde ao nome vindo da WMI.</summary>
        private static string MatchSensorHardware(IReadOnlyList<SensorReading> readings, HardwareKind kind, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var names = readings.Where(r => r.HardwareKind == kind).Select(r => r.HardwareName).Distinct().ToList();
            return names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                   ?? names.FirstOrDefault(n => n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                                                || name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
