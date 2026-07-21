// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.InteropServices;
using Aetheus.Agent.Core.Collectors;

namespace Aetheus.Agent.Windows.Collectors;

public sealed class WindowsMetricsCollector(ILogger<WindowsMetricsCollector> logger) : BaseMetricsCollector
{
    // GetSystemTimes replaces the previous Process.GetProcesses() sweep, which cost O(number
    // of processes) and leaked process handles on every heartbeat.
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _cpuInitialized;

    protected override double GetCpuPercent()
    {
        try
        {
            if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                return 0;

            var idle = ToUInt64(idleFt);
            var kernel = ToUInt64(kernelFt);
            var user = ToUInt64(userFt);

            if (!_cpuInitialized)
            {
                (_previousIdle, _previousKernel, _previousUser) = (idle, kernel, user);
                _cpuInitialized = true;
                return 0;
            }

            var idleDelta = idle - _previousIdle;
            // Kernel time includes idle time, so kernel+user is the total.
            var totalDelta = (kernel - _previousKernel) + (user - _previousUser);
            (_previousIdle, _previousKernel, _previousUser) = (idle, kernel, user);

            if (totalDelta == 0) return 0;
            var result = Math.Round((1.0 - (double)idleDelta / totalDelta) * 100, 1);
            return Math.Clamp(result, 0, 100);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to get CPU percent");
            return 0;
        }
    }

    private static ulong ToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME ft) =>
        ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    protected override double GetMemoryUsedMb()
    {
        try
        {
            var memoryStatus = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref memoryStatus))
                return Math.Round((memoryStatus.TotalPhysical - memoryStatus.AvailablePhysical) / 1024.0 / 1024.0, 0);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to get memory used");
            return 0;
        }
    }

    protected override double GetMemoryTotalMb()
    {
        try
        {
            var memoryStatus = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref memoryStatus))
                return Math.Round(memoryStatus.TotalPhysical / 1024.0 / 1024.0, 0);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to get total memory");
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);
}
