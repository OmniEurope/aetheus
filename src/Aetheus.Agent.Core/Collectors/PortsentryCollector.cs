// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Aetheus.Shared.DTOs;

namespace Aetheus.Agent.Core.Collectors;

public sealed partial class PortsentryCollector(
    ILogger<PortsentryCollector> logger,
    IShellRunner shell,
    Func<string, bool>? fileExists = null,
    Func<string, string>? readAllText = null,
    Func<string, CancellationToken, Task<string>>? readAllTextAsync = null)
    : BaseShellCollector<PortsentryCollector>(logger, shell), IPortsentryCollector
{
    private static readonly string[] KnownPaths =
    [
        "/usr/sbin/portsentry",
        "/usr/local/sbin/portsentry"
    ];

    private const string ConfigPath = "/etc/portsentry/portsentry.conf";
    private const string BlockedTcpPath = "/etc/portsentry/portsentry.blocked.atcp";
    private const string BlockedUdpPath = "/etc/portsentry/portsentry.blocked.audp";

    public async Task<PortsentryDataDto> CollectAsync(CancellationToken ct = default)
    {
        try
        {
            var binaryPath = await DetectBinaryAsync(ct).ConfigureAwait(false);
            if (binaryPath is null)
                return new PortsentryDataDto();

            var isRunning = await IsRunningAsync(ct).ConfigureAwait(false);
            var version = await GetVersionAsync(ct).ConfigureAwait(false);
            var mode = GetMode();
            var (tcpPorts, udpPorts) = GetMonitoredPorts();
            var blockedIps = await GetBlockedIpsAsync(ct).ConfigureAwait(false);

            return new PortsentryDataDto
            {
                IsInstalled = true,
                IsRunning = isRunning,
                Version = version,
                Mode = mode,
                TcpPorts = tcpPorts,
                UdpPorts = udpPorts,
                BlockedCount = blockedIps.Count,
                BlockedIps = blockedIps
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to collect PortSentry data");
            return new PortsentryDataDto();
        }
    }

    private async Task<string?> DetectBinaryAsync(CancellationToken ct)
    {
        foreach (var path in KnownPaths)
        {
            if ((fileExists ?? File.Exists)(path))
                return path;
        }

        var res = await Shell.RunExecAsync("which", ["portsentry"], ct).ConfigureAwait(false);
        return res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.StdOut) ? res.StdOut.Trim() : null;
    }

    private async Task<bool> IsRunningAsync(CancellationToken ct)
    {
        var svc = await Shell.RunExecAsync("systemctl", ["is-active", "portsentry"], ct).ConfigureAwait(false);
        if (svc.StdOut.Trim().Equals("active", StringComparison.OrdinalIgnoreCase))
            return true;

        var pidof = await Shell.RunExecAsync("pidof", ["portsentry"], ct).ConfigureAwait(false);
        return pidof.ExitCode == 0 && !string.IsNullOrWhiteSpace(pidof.StdOut);
    }

    private async Task<string> GetVersionAsync(CancellationToken ct)
    {
        try
        {
            // Was: dpkg -l portsentry | grep '^ii' | awk '{print $3}'  (Debian)
            var dpkg = await Shell.RunExecAsync("dpkg", ["-l", "portsentry"], ct).ConfigureAwait(false);
            foreach (var line in dpkg.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("ii", StringComparison.Ordinal)) continue;
                var cols = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length >= 3) return cols[2];
            }

            // Fallback: rpm -q portsentry --qf '%{VERSION}'  (RHEL)
            var rpm = await Shell.RunExecAsync("rpm", ["-q", "portsentry", "--qf", "%{VERSION}"], ct).ConfigureAwait(false);
            return rpm.ExitCode == 0 ? rpm.StdOut.Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ReadConfigLines()
    {
        try
        {
            return (fileExists ?? File.Exists)(ConfigPath)
                ? (readAllText ?? File.ReadAllText)(ConfigPath)
                : string.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to read {Path}", ConfigPath);
            return string.Empty;
        }
    }

    private string GetMode()
    {
        var relevant = ReadConfigLines()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.TrimStart().StartsWith("TCP_MODE", StringComparison.Ordinal)
                     || l.TrimStart().StartsWith("BLOCK_TCP", StringComparison.Ordinal));
        var joined = string.Join('\n', relevant);

        if (joined.Contains("atcp", StringComparison.OrdinalIgnoreCase))
            return "atcp";
        if (joined.Contains("stcp", StringComparison.OrdinalIgnoreCase))
            return "stcp";
        return "tcp";
    }

    private (string TcpPorts, string UdpPorts) GetMonitoredPorts()
    {
        var tcp = string.Empty;
        var udp = string.Empty;

        foreach (var line in ReadConfigLines().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("TCP_PORTS=", StringComparison.Ordinal))
                tcp = trimmed["TCP_PORTS=".Length..].Trim('"');
            else if (trimmed.StartsWith("UDP_PORTS=", StringComparison.Ordinal))
                udp = trimmed["UDP_PORTS=".Length..].Trim('"');
        }

        return (tcp, udp);
    }

    private async Task<List<PortsentryBlockedIpDto>> GetBlockedIpsAsync(CancellationToken ct)
    {
        var result = new List<PortsentryBlockedIpDto>();
        try
        {
            await ParseBlockedFileAsync(BlockedTcpPath, "TCP", result, ct).ConfigureAwait(false);
            await ParseBlockedFileAsync(BlockedUdpPath, "UDP", result, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to parse PortSentry blocked files");
        }
        return result;
    }

    private async Task ParseBlockedFileAsync(string path, string protocol, List<PortsentryBlockedIpDto> result, CancellationToken ct)
    {
        string output;
        try
        {
            output = (fileExists ?? File.Exists)(path)
                ? await (readAllTextAsync ?? File.ReadAllTextAsync)(path, ct).ConfigureAwait(false)
                : string.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to read {Path}", path);
            return;
        }

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var ip = parts.Length > 0 ? parts[0].Trim() : trimmed;

            if (!IpAddressRegex().IsMatch(ip))
                continue;

            result.Add(new PortsentryBlockedIpDto
            {
                IpAddress = ip,
                Protocol = protocol,
                Reason = "Blocked by PortSentry"
            });
        }
    }

    [GeneratedRegex(@"^(\d{1,3}\.){3}\d{1,3}$|^[0-9a-fA-F:]+$")]
    private static partial Regex IpAddressRegex();
}
