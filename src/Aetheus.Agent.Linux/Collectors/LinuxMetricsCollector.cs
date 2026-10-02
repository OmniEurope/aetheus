// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Collectors;

namespace Aetheus.Agent.Linux.Collectors;

public sealed class LinuxMetricsCollector(ILogger<LinuxMetricsCollector> logger) : BaseMetricsCollector
{
    private long _previousIdle;
    private long _previousTotal;
    private bool _cpuInitialized;

    protected override double GetCpuPercent()
    {
        try
        {
            var cpuLine = File.ReadAllLines("/proc/stat")
                .First(l => l.StartsWith("cpu "));
            var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // parts[1..] = user, nice, system, idle, iowait, irq, softirq, steal
            var values = parts.Skip(1).Select(long.Parse).ToArray();
            var idle = values[3] + (values.Length > 4 ? values[4] : 0); // idle + iowait
            var total = values.Sum();

            if (!_cpuInitialized)
            {
                _previousIdle = idle;
                _previousTotal = total;
                _cpuInitialized = true;
                return 0;
            }

            var deltaTotal = total - _previousTotal;
            var deltaIdle = idle - _previousIdle;

            _previousIdle = idle;
            _previousTotal = total;

            if (deltaTotal <= 0) return 0;
            return Math.Round((1.0 - (double)deltaIdle / deltaTotal) * 100, 1);
        }
        catch (Exception ex)
        {
            // F-ENG-06: a /proc read failure returns 0% which is indistinguishable from a healthy idle
            // system - surface it at Warning so the panne is visible, not silently reported as 0.
            logger.LogWarning(ex, "Failed to read /proc/stat for CPU percent; reporting 0 (may look idle)");
            return 0;
        }
    }

    protected override double GetMemoryUsedMb()
    {
        try
        {
            var memInfo = File.ReadAllLines("/proc/meminfo");
            var total = ParseMemInfoLine(memInfo.First(l => l.StartsWith("MemTotal:")));
            var available = ParseMemInfoLine(memInfo.First(l => l.StartsWith("MemAvailable:")));
            return Math.Round((total - available) / 1024.0, 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read /proc/meminfo for memory used; reporting 0"); // F-ENG-06
            return 0;
        }
    }

    protected override double GetMemoryTotalMb()
    {
        try
        {
            var memInfo = File.ReadAllLines("/proc/meminfo");
            var total = ParseMemInfoLine(memInfo.First(l => l.StartsWith("MemTotal:")));
            return Math.Round(total / 1024.0, 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read /proc/meminfo for total memory; reporting 0"); // F-ENG-06
            return 0;
        }
    }

    protected override bool ShouldIncludeDrive(DriveInfo drive)
    {
        return drive.Name.StartsWith("/dev") ||
               drive.Name == "/" ||
               drive.Name.StartsWith("/home") ||
               drive.Name.StartsWith("/mnt");
    }

    private static double ParseMemInfoLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return double.Parse(parts[1]); // value in kB
    }
}
