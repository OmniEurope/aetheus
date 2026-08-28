// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace Aetheus.Agent.Core.Operations;

public sealed class ScannerProcessRunner(
    IOptions<Configuration.AetheusAgentOptions> options,
    ILogger<ScannerProcessRunner> logger) : IScannerProcessRunner
{
    private static readonly TimeSpan s_monitorShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly Meter s_meter = new("Aetheus.Analysis.Agent");
    private static readonly Histogram<double> s_duration = s_meter.CreateHistogram<double>(
        "aetheus.analysis.scanner.duration", "s", "Elapsed scanner execution time.");
    private static readonly Histogram<double> s_cpu = s_meter.CreateHistogram<double>(
        "aetheus.analysis.scanner.cpu", "%", "Peak scanner container CPU percentage.");
    private static readonly Histogram<long> s_memory = s_meter.CreateHistogram<long>(
        "aetheus.analysis.scanner.memory", "By", "Peak scanner container memory usage.");
    private static readonly Histogram<long> s_disk = s_meter.CreateHistogram<long>(
        "aetheus.analysis.scanner.disk", "By", "Scanner container block I/O bytes.");
    private static readonly Histogram<long> s_network = s_meter.CreateHistogram<long>(
        "aetheus.analysis.scanner.network", "By", "Scanner container network I/O bytes.");
    private readonly Configuration.AetheusAgentOptions _options = options.Value;

    public async Task<ExecutorResult> RunAsync(
        ProcessStartInfo processInfo,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken ct)
    {
        timeoutSeconds = Math.Clamp(timeoutSeconds, _options.MinTimeoutSeconds, _options.MaxTimeoutSeconds);
        using var process = new Process { StartInfo = processInfo };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var stopwatch = Stopwatch.StartNew();
        var containerName = TryGetContainerName(processInfo);
        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        process.Start();
        var monitor = containerName is null
            ? Task.FromResult(ContainerUsage.Empty)
            : MonitorContainerAsync(containerName, monitorCancellation.Token);
        var stdout = ExecutorHelper.StreamOutputAsync(process.StandardOutput, TaskLogLevel.Info, onOutput, timeout.Token, logger);
        var stderr = ExecutorHelper.StreamOutputAsync(process.StandardError, TaskLogLevel.Error, onOutput, timeout.Token, logger);
        try
        {
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await monitorCancellation.CancelAsync().ConfigureAwait(false);
            var usage = await AwaitMonitorAsync(monitor).ConfigureAwait(false);
            await PublishUsageAsync(stopwatch.Elapsed, usage, onOutput).ConfigureAwait(false);
            return new ExecutorResult(process.ExitCode, false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) { logger.LogDebug(ex, "Scanner process kill failed"); }
            await monitorCancellation.CancelAsync().ConfigureAwait(false);
            var usage = await AwaitMonitorAsync(monitor).ConfigureAwait(false);
            await PublishUsageAsync(stopwatch.Elapsed, usage, onOutput).ConfigureAwait(false);
            if (ct.IsCancellationRequested) throw;
            return new ExecutorResult(-1, true);
        }
    }

    private async Task<ContainerUsage> AwaitMonitorAsync(Task<ContainerUsage> monitor)
    {
        try
        {
            if (!await CompletesWithinAsync(monitor, s_monitorShutdownTimeout).ConfigureAwait(false))
            {
                logger.LogWarning(
                    "Scanner resource monitor did not stop within {TimeoutSeconds}s; continuing task finalization without usage metrics",
                    s_monitorShutdownTimeout.TotalSeconds);
                return ContainerUsage.Empty;
            }

            return await monitor.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Scanner resource monitor failed during task finalization");
            return ContainerUsage.Empty;
        }
    }

    internal static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static string? TryGetContainerName(ProcessStartInfo process)
    {
        if (!string.Equals(process.FileName, "docker", StringComparison.OrdinalIgnoreCase)) return null;
        for (var index = 0; index < process.ArgumentList.Count - 1; index++)
            if (string.Equals(process.ArgumentList[index], "--name", StringComparison.Ordinal))
                return process.ArgumentList[index + 1];
        return null;
    }

    private static async Task<ContainerUsage> MonitorContainerAsync(string containerName, CancellationToken ct)
    {
        var peakCpu = 0d;
        long peakMemory = 0;
        long diskRead = 0;
        long diskWritten = 0;
        long networkReceived = 0;
        long networkSent = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var startInfo = new ProcessStartInfo("docker")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var argument in new[]
                         {
                             "stats", "--no-stream", "--format",
                             "{{.CPUPerc}}|{{.MemUsage}}|{{.BlockIO}}|{{.NetIO}}", containerName
                         })
                    startInfo.ArgumentList.Add(argument);
                using var stats = Process.Start(startInfo);
                if (stats is not null)
                {
                    var output = await stats.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
                    await stats.WaitForExitAsync(ct).ConfigureAwait(false);
                    if (stats.ExitCode == 0 && TryParseUsage(
                            output,
                            out var cpu,
                            out var memory,
                            out var readBytes,
                            out var writtenBytes,
                            out var receivedBytes,
                            out var sentBytes))
                    {
                        peakCpu = Math.Max(peakCpu, cpu);
                        peakMemory = Math.Max(peakMemory, memory);
                        diskRead = Math.Max(diskRead, readBytes);
                        diskWritten = Math.Max(diskWritten, writtenBytes);
                        networkReceived = Math.Max(networkReceived, receivedBytes);
                        networkSent = Math.Max(networkSent, sentBytes);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        return new ContainerUsage(
            peakCpu, peakMemory, diskRead, diskWritten, networkReceived, networkSent);
    }

    internal static bool TryParseUsage(string value, out double cpuPercent, out long memoryBytes)
    {
        cpuPercent = 0;
        memoryBytes = 0;
        var parts = value.Trim().Split('|', 2);
        if (parts.Length != 2
            || !double.TryParse(parts[0].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out cpuPercent))
            return false;
        var memory = parts[1].Split('/', 2)[0].Trim();
        return TryParseBytes(memory, out memoryBytes);
    }

    internal static bool TryParseUsage(
        string value,
        out double cpuPercent,
        out long memoryBytes,
        out long diskReadBytes,
        out long diskWrittenBytes,
        out long networkReceivedBytes,
        out long networkSentBytes)
    {
        cpuPercent = 0;
        memoryBytes = 0;
        diskReadBytes = 0;
        diskWrittenBytes = 0;
        networkReceivedBytes = 0;
        networkSentBytes = 0;
        var parts = value.Trim().Split('|', 4);
        return parts.Length == 4
               && TryParseUsage($"{parts[0]}|{parts[1]}", out cpuPercent, out memoryBytes)
               && TryParseIoPair(parts[2], out diskReadBytes, out diskWrittenBytes)
               && TryParseIoPair(parts[3], out networkReceivedBytes, out networkSentBytes);
    }

    private static bool TryParseIoPair(string value, out long first, out long second)
    {
        first = 0;
        second = 0;
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2
               && TryParseBytes(parts[0], out first)
               && TryParseBytes(parts[1], out second);
    }

    private static bool TryParseBytes(string value, out long bytes)
    {
        bytes = 0;
        var unitStart = value.TakeWhile(character => char.IsDigit(character) || character is '.' or ',').Count();
        if (unitStart == 0 || !double.TryParse(value[..unitStart].Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var amount)) return false;
        var unit = value[unitStart..].Trim().ToUpperInvariant();
        var factor = unit switch
        {
            "B" => 1d,
            "KB" => 1_000d,
            "KIB" => 1_024d,
            "MB" => 1_000_000d,
            "MIB" => 1_048_576d,
            "GB" => 1_000_000_000d,
            "GIB" => 1_073_741_824d,
            _ => 0d
        };
        if (factor == 0) return false;
        bytes = checked((long)Math.Round(amount * factor));
        return true;
    }

    private static async Task PublishUsageAsync(
        TimeSpan elapsed,
        ContainerUsage usage,
        Func<string, TaskLogLevel, Task> onOutput)
    {
        s_duration.Record(elapsed.TotalSeconds);
        if (usage.PeakCpuPercent > 0) s_cpu.Record(usage.PeakCpuPercent);
        if (usage.PeakMemoryBytes > 0) s_memory.Record(usage.PeakMemoryBytes);
        if (usage.DiskReadBytes + usage.DiskWrittenBytes > 0)
            s_disk.Record(usage.DiskReadBytes + usage.DiskWrittenBytes);
        if (usage.NetworkReceivedBytes + usage.NetworkSentBytes > 0)
            s_network.Record(usage.NetworkReceivedBytes + usage.NetworkSentBytes);
        await onOutput(
            $"Scanner resource usage: duration={elapsed.TotalSeconds:0.###}s, peakCpu={usage.PeakCpuPercent:0.##}%, " +
            $"peakMemory={usage.PeakMemoryBytes}B, swap=unavailable, diskRead={usage.DiskReadBytes}B, " +
            $"diskWritten={usage.DiskWrittenBytes}B, networkReceived={usage.NetworkReceivedBytes}B, " +
            $"networkSent={usage.NetworkSentBytes}B.",
            TaskLogLevel.Info).ConfigureAwait(false);
        await PipelineMetricEmitter.EmitAsync(onOutput, "scanner.duration", "Duration", "s", elapsed.TotalSeconds).ConfigureAwait(false);
        await PipelineMetricEmitter.EmitAsync(onOutput, "scanner.cpu.peak", "Percentage", "%", usage.PeakCpuPercent).ConfigureAwait(false);
        await PipelineMetricEmitter.EmitAsync(onOutput, "scanner.memory.peak", "Size", "bytes", usage.PeakMemoryBytes).ConfigureAwait(false);
        await PipelineMetricEmitter.EmitAsync(onOutput, "scanner.disk.read", "Size", "bytes", usage.DiskReadBytes).ConfigureAwait(false);
        await PipelineMetricEmitter.EmitAsync(onOutput, "scanner.disk.written", "Size", "bytes", usage.DiskWrittenBytes).ConfigureAwait(false);
        await PipelineMetricEmitter.EmitAsync(onOutput, "scanner.network.received", "Size", "bytes", usage.NetworkReceivedBytes).ConfigureAwait(false);
        await PipelineMetricEmitter.EmitAsync(onOutput, "scanner.network.sent", "Size", "bytes", usage.NetworkSentBytes).ConfigureAwait(false);
    }

    private sealed record ContainerUsage(
        double PeakCpuPercent,
        long PeakMemoryBytes,
        long DiskReadBytes,
        long DiskWrittenBytes,
        long NetworkReceivedBytes,
        long NetworkSentBytes)
    {
        public static ContainerUsage Empty { get; } = new(0, 0, 0, 0, 0, 0);
    }
}
