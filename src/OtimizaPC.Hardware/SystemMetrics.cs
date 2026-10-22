using System;
using System.Linq;
using System.Net.NetworkInformation;
using OtimizaPC.Hardware.Interop;

namespace OtimizaPC.Hardware
{
    public sealed class MemoryUsage
    {
        public MemoryUsage(ulong total, ulong available)
        {
            Total = total;
            Available = available;
        }

        public ulong Total { get; }

        public ulong Available { get; }

        public ulong Used => Total > Available ? Total - Available : 0;

        public double Percent => Total == 0 ? 0 : Used * 100.0 / Total;
    }

    public sealed class NetworkRate
    {
        public NetworkRate(double download, double upload)
        {
            DownloadBytesPerSecond = download;
            UploadBytesPerSecond = upload;
        }

        public double DownloadBytesPerSecond { get; }

        public double UploadBytesPerSecond { get; }
    }

    /// <summary>
    /// Métricas em tempo real via API Win32 (não depende de contadores de desempenho,
    /// que têm nomes traduzidos e às vezes estão corrompidos).
    /// </summary>
    public sealed class SystemMetrics
    {
        private ulong _prevIdle;
        private ulong _prevTotal;
        private long _prevRx;
        private long _prevTx;
        private DateTime _prevNetTime;

        public double GetCpuUsage()
        {
            if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
                return 0;

            // O tempo de kernel já inclui o tempo ocioso
            var total = kernel.Value + user.Value;
            var deltaTotal = total - _prevTotal;
            var deltaIdle = idle.Value - _prevIdle;
            _prevTotal = total;
            _prevIdle = idle.Value;

            if (deltaTotal == 0) return 0;
            var usage = (1.0 - (double)deltaIdle / deltaTotal) * 100.0;
            return Math.Max(0, Math.Min(100, usage));
        }

        public static MemoryUsage GetMemory()
        {
            var status = new NativeMethods.MEMORYSTATUSEX();
            return NativeMethods.GlobalMemoryStatusEx(status)
                ? new MemoryUsage(status.ullTotalPhys, status.ullAvailPhys)
                : new MemoryUsage(0, 0);
        }

        public static TimeSpan GetUptime() => TimeSpan.FromMilliseconds(NativeMethods.GetTickCount64());

        public NetworkRate GetNetworkRate()
        {
            long rx = 0, tx = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(IsPhysicalUp))
            {
                try
                {
                    var stats = nic.GetIPv4Statistics();
                    rx += stats.BytesReceived;
                    tx += stats.BytesSent;
                }
                catch
                {
                    // Alguns adaptadores virtuais não expõem estatísticas
                }
            }

            var now = DateTime.UtcNow;
            var seconds = (now - _prevNetTime).TotalSeconds;
            var first = _prevNetTime == default;
            var rate = first || seconds <= 0
                ? new NetworkRate(0, 0)
                : new NetworkRate(Math.Max(0, (rx - _prevRx) / seconds), Math.Max(0, (tx - _prevTx) / seconds));

            _prevRx = rx;
            _prevTx = tx;
            _prevNetTime = now;
            return rate;
        }

        private static bool IsPhysicalUp(NetworkInterface nic) =>
            nic.OperationalStatus == OperationalStatus.Up
            && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
            && nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel;
    }
}
