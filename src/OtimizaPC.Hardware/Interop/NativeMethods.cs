using System;
using System.Runtime.InteropServices;

namespace OtimizaPC.Hardware.Interop
{
    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct FILETIME
        {
            public uint Low;
            public uint High;

            public ulong Value => ((ulong)High << 32) | Low;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal sealed class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
        {
            public long IdleTime;
            public long KernelTime;
            public long UserTime;
            public long DpcTime;
            public long InterruptTime;
            public uint InterruptCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

        /// <summary>
        /// SystemProcessorPerformanceInformation (classe 8): tempos por processador lógico.
        /// KernelTime já inclui o tempo ocioso.
        /// </summary>
        [DllImport("ntdll.dll")]
        internal static extern int NtQuerySystemInformation(
            int systemInformationClass,
            IntPtr systemInformation,
            int systemInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll")]
        internal static extern ulong GetTickCount64();

        /// <summary>1 = BIOS, 2 = UEFI. Disponível a partir do Windows 8 / Server 2012.</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFirmwareType(out int firmwareType);
    }
}
