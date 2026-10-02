// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Globalization;

namespace Aetheus.Agent.Core.Executors;

public sealed record ProcessResourceUsage(
    TimeSpan CpuTime,
    long PeakWorkingSetBytes,
    long PeakSwapBytes,
    long DiskReadBytes,
    long DiskWrittenBytes);

internal readonly record struct ProcessResourceSample(
    int ProcessId,
    long StartedAtTicks,
    TimeSpan CpuTime,
    long WorkingSetBytes,
    long SwapBytes,
    long DiskReadBytes,
    long DiskWrittenBytes);

internal sealed class ProcessResourceAccumulator
{
    private readonly Dictionary<(int ProcessId, long StartedAtTicks), TimeSpan> _cpu = [];
    private readonly Dictionary<(int ProcessId, long StartedAtTicks), long> _diskRead = [];
    private readonly Dictionary<(int ProcessId, long StartedAtTicks), long> _diskWritten = [];
    private long _peakWorkingSet;
    private long _peakSwap;

    internal void Add(IEnumerable<ProcessResourceSample> samples)
    {
        long workingSet = 0;
        long swap = 0;
        foreach (var sample in samples)
        {
            var identity = (sample.ProcessId, sample.StartedAtTicks);
            _cpu[identity] = Max(_cpu.GetValueOrDefault(identity), sample.CpuTime);
            _diskRead[identity] = Math.Max(_diskRead.GetValueOrDefault(identity), sample.DiskReadBytes);
            _diskWritten[identity] = Math.Max(_diskWritten.GetValueOrDefault(identity), sample.DiskWrittenBytes);
            workingSet = SaturatingAdd(workingSet, sample.WorkingSetBytes);
            swap = SaturatingAdd(swap, sample.SwapBytes);
        }

        _peakWorkingSet = Math.Max(_peakWorkingSet, workingSet);
        _peakSwap = Math.Max(_peakSwap, swap);
    }

    internal ProcessResourceUsage Build() => new(
        TimeSpan.FromTicks(_cpu.Values.Select(static value => value.Ticks).Aggregate(0L, SaturatingAdd)),
        _peakWorkingSet,
        _peakSwap,
        _diskRead.Values.Aggregate(0L, SaturatingAdd),
        _diskWritten.Values.Aggregate(0L, SaturatingAdd));

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private static long SaturatingAdd(long left, long right) =>
        right <= 0 ? left : left > long.MaxValue - right ? long.MaxValue : left + right;
}

public static class ProcessResourceMonitor
{
    public static async Task<ProcessResourceUsage> MonitorAsync(Process process, CancellationToken ct)
    {
        var accumulator = new ProcessResourceAccumulator();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                accumulator.Add(CaptureTree(process));
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (InvalidOperationException) { } // the process exited between two samples; the final capture below closes the series
        accumulator.Add(CaptureTree(process));
        return accumulator.Build();
    }

    public static async Task PublishAsync(
        ProcessResourceUsage usage,
        Func<string, TaskLogLevel, Task> onOutput)
    {
        await onOutput(
            $"Step resource usage: cpu={usage.CpuTime.TotalSeconds:0.###}s, peakRss={usage.PeakWorkingSetBytes}B, " +
            $"peakSwap={usage.PeakSwapBytes}B, diskRead={usage.DiskReadBytes}B, " +
            $"diskWritten={usage.DiskWrittenBytes}B, network=unavailable for host processes.",
            TaskLogLevel.Info).ConfigureAwait(false);
        await Metric(onOutput, "step.cpu", "Duration", "s", usage.CpuTime.TotalSeconds).ConfigureAwait(false);
        await Metric(onOutput, "step.memory.peak", "Size", "bytes", usage.PeakWorkingSetBytes).ConfigureAwait(false);
        await Metric(onOutput, "step.swap.peak", "Size", "bytes", usage.PeakSwapBytes).ConfigureAwait(false);
        await Metric(onOutput, "step.disk.read", "Size", "bytes", usage.DiskReadBytes).ConfigureAwait(false);
        await Metric(onOutput, "step.disk.written", "Size", "bytes", usage.DiskWrittenBytes).ConfigureAwait(false);
    }

    internal static IReadOnlyList<ProcessResourceSample> CaptureTree(Process root)
    {
        var samples = new List<ProcessResourceSample>();
        var descendants = ProcessTreeSnapshot.CaptureDescendants(root.Id);
        try
        {
            AddSample(root, samples);
            foreach (var descendant in descendants)
                AddSample(descendant, samples);
        }
        finally
        {
            foreach (var descendant in descendants)
                descendant.Dispose();
        }
        return samples;
    }

    private static void AddSample(Process process, ICollection<ProcessResourceSample> samples)
    {
        try
        {
            process.Refresh();
            var swap = 0L;
            var diskRead = 0L;
            var diskWritten = 0L;
            if (OperatingSystem.IsLinux())
            {
                var processRoot = $"/proc/{process.Id}";
                if (File.Exists(Path.Combine(processRoot, "status")))
                {
                    foreach (var line in File.ReadLines(Path.Combine(processRoot, "status")))
                        if (line.StartsWith("VmSwap:", StringComparison.Ordinal))
                            swap = ParseProcKilobytes(line);
                }
                if (File.Exists(Path.Combine(processRoot, "io")))
                {
                    foreach (var line in File.ReadLines(Path.Combine(processRoot, "io")))
                    {
                        if (line.StartsWith("read_bytes:", StringComparison.Ordinal))
                            diskRead = ParseProcBytes(line);
                        else if (line.StartsWith("write_bytes:", StringComparison.Ordinal))
                            diskWritten = ParseProcBytes(line);
                    }
                }
            }

            samples.Add(new ProcessResourceSample(
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                process.TotalProcessorTime,
                process.WorkingSet64,
                swap,
                diskRead,
                diskWritten));
        }
        catch (Exception error) when (error is InvalidOperationException or IOException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // The process exited or its /proc entry vanished while it was read: it drops out of this sample.
        }
    }

    private static long ParseProcKilobytes(string line) => checked(ParseProcBytes(line) * 1024);

    private static long ParseProcBytes(string line)
    {
        var digits = new string(line.SkipWhile(character => !char.IsDigit(character)).TakeWhile(char.IsDigit).ToArray());
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static Task Metric(
        Func<string, TaskLogLevel, Task> onOutput,
        string key,
        string type,
        string unit,
        double value) =>
        onOutput(
            $"##aetheus[pipelinemetric key={key};type={type};unit={unit}]" +
            value.ToString("0.######", CultureInfo.InvariantCulture),
            TaskLogLevel.Info);
}
