// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Text.RegularExpressions;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Services;

/// <summary>
/// Owns one named Buildx cache per agent. It never invokes a global prune, removes an image, container
/// or volume, and serializes maintenance with builds through one exclusive gate.
/// </summary>
public sealed class DockerStorageMaintenanceService(
    IShellRunner shell,
    IOptions<AetheusAgentOptions> options,
    TimeProvider timeProvider,
    ILogger<DockerStorageMaintenanceService> logger) : BackgroundService, IDockerStorageMaintenance
{
    private const int MaxDiagnosticFiles = 1_000_000;
    private const string CommandWrapperPattern =
        @"(?:(?:(?:[^\s;&|]+/)?(?:sudo|env|command|exec|nohup))\s+(?:(?:-[^\s;&|]+|[A-Za-z_][A-Za-z0-9_]*=[^\s;&|]+|[A-Za-z0-9_.:@/-]+)\s+)*)*";
    private static readonly Regex DockerBuildPattern = new(
        @"(?:^|[;&|\n]\s*)" + CommandWrapperPattern
        + @"(?:[^\s;&|]+/)?docker\s+(?:build(?:\s|$)|buildx\s+build(?:\s|$)|compose\b[^\n;&|]*(?:\sbuild(?:\s|$)|\s--build(?:\s|$)))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex ComposeVariableBuildPattern = new(
        @"(?:^|[;&|\n]\s*)" + CommandWrapperPattern
        + @"\$(?:COMPOSE\b|\{COMPOSE\})[^\n;&|]*(?:\sbuild(?:\s|$)|\s--build(?:\s|$))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex ShellWrappedCommandPattern = new(
        @"(?:^|[;&|\n]\s*)" + CommandWrapperPattern
        + @"(?:[^\s;&|]+/)?(?:bash|dash|sh|zsh)\s+(?:-[^\s;&|]+\s+)*(?<quote>[""'])(?<command>.*?)\k<quote>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex DeploymentBuildPattern = new(
        @"(?<![A-Za-z0-9_.-])(?:[^\s;&|]+/)?docker(?:\.exe)?\s+"
        + @"(?:(?:(?:--context|--host|--config|--log-level|-H)(?:=[^\s;&|]+|\s+[^\s;&|]+))\s+)*"
        + @"(?:build(?:\s|$)|buildx\s+build(?:\s|$)|compose\b[^\n;&|]*(?:\sbuild(?:\s|$)|\s--build(?:\s|$)))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly AetheusAgentOptions _agentOptions = options.Value;
    private readonly DockerStorageMaintenanceOptions _options = options.Value.DockerStorageMaintenance;
    private readonly SemaphoreSlim _exclusiveGate = new(1, 1);
    private bool _buildGateHeld;
    private long _lastBuildAttemptUtcTicks;

    public string BuilderName => ResolveBuilderName(_options.BuilderName, _agentOptions.Name);
    public bool DeploymentOnly => _options.DeploymentOnly;
    private bool IsDryRun => _options.DryRun;

    public bool IsBuildCommand(string command) => IsBuildCommand(command, depth: 0);

    private bool IsBuildCommand(string command, int depth)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        // Check the narrow variable form first. The broader wrapper expression can otherwise
        // spend its entire timeout backtracking over a preceding COMPOSE assignment.
        if (ComposeVariableBuildPattern.IsMatch(command)
            || DockerBuildPattern.IsMatch(command)
            || (_options.DeploymentOnly && DeploymentBuildPattern.IsMatch(command)))
        {
            return true;
        }
        if (depth >= 3) return false;

        return ShellWrappedCommandPattern.Matches(command)
            .Select(match => match.Groups["command"].Value)
            .Any(nested => IsBuildCommand(nested, depth + 1));
    }

    public async Task PrepareBuildAsync(
        IDictionary<string, string> environmentVariables,
        CancellationToken ct,
        bool runDockerMaintenance = true)
    {
        Interlocked.Exchange(ref _lastBuildAttemptUtcTicks, timeProvider.GetUtcNow().UtcDateTime.Ticks);
        if (_options.DeploymentOnly && !_options.AllowBuildsOnDeploymentTarget)
        {
            throw new InvalidOperationException(
                "Docker build refused: this agent is configured as a deployment-only target. " +
                "Use a dedicated build runner or explicitly audit and enable AllowBuildsOnDeploymentTarget.");
        }

        await _exclusiveGate.WaitAsync(ct).ConfigureAwait(false);
        _buildGateHeld = true;
        try
        {
            environmentVariables["AETHEUS_BUILDX_BUILDER"] = BuilderName;
            environmentVariables["BUILDX_BUILDER"] = BuilderName;
            if (!_options.Enabled || !runDockerMaintenance) return;

            await EnsureBuilderAsync(ct).ConfigureAwait(false);
            var (usedPercent, freeBytes) = GetDiskState();
            if (ShouldUseAggressivePolicy(usedPercent, freeBytes, _options))
                await RunMaintenanceUnderLockAsync("pre-build disk pressure", ct).ConfigureAwait(false);

            (_, freeBytes) = GetDiskState();
            if (!IsDryRun && freeBytes < GiB(_options.MinFreeSpaceGiB))
            {
                throw new InvalidOperationException(
                    $"Docker build refused after bounded cache maintenance: only {FormatBytes(freeBytes)} free; " +
                    $"the configured safety floor is {_options.MinFreeSpaceGiB} GiB.");
            }
        }
        catch
        {
            ReleaseBuildGate();
            throw;
        }
    }

    public async Task CompleteBuildAsync(CancellationToken ct, bool runDockerMaintenance = true)
    {
        if (!_buildGateHeld) return;
        try
        {
            if (_options.Enabled && runDockerMaintenance)
                await RunMaintenanceUnderLockAsync("post-build finalization", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A completed build remains truthful. Cleanup failure is observable but never rewrites its result.
            logger.LogError(ex, "Docker storage maintenance failed after build; build result is preserved");
        }
        finally
        {
            ReleaseBuildGate();
        }
    }

    public async Task RunMaintenanceAsync(string reason, CancellationToken ct)
    {
        if (!_options.Enabled) return;
        if (!await _exclusiveGate.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            logger.LogInformation("Skipping Docker storage maintenance ({Reason}): a build or cleanup owns the lock", reason);
            return;
        }

        try
        {
            await RunMaintenanceUnderLockAsync(reason, ct).ConfigureAwait(false);
        }
        finally
        {
            _exclusiveGate.Release();
        }
    }

    public async Task<StorageDiagnosticsDto> CollectDiagnosticsAsync(CancellationToken ct)
    {
        var builder = await InspectBuilderBytesAsync(ct).ConfigureAwait(false);
        long images = 0;
        long containers = 0;
        long volumes = 0;
        var docker = await shell.RunExecAsync("docker",
            ["system", "df", "--format", "{{.Type}}\t{{.Size}}\t{{.Reclaimable}}"],
            ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (docker.ExitCode == 0)
        {
            foreach (var line in docker.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split('\t', StringSplitOptions.TrimEntries);
                if (parts.Length < 2) continue;
                var bytes = ParseSizeBytes(parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
                if (parts[0].Equals("Images", StringComparison.OrdinalIgnoreCase)) images = bytes;
                else if (parts[0].Equals("Containers", StringComparison.OrdinalIgnoreCase)) containers = bytes;
                else if (parts[0].Contains("Volumes", StringComparison.OrdinalIgnoreCase)) volumes = bytes;
            }
        }

        var nugetPath = ResolveNuGetPackagesPath();
        var lastBuildAttemptTicks = Interlocked.Read(ref _lastBuildAttemptUtcTicks);
        return new StorageDiagnosticsDto
        {
            BuilderName = BuilderName,
            DryRun = IsDryRun,
            DeploymentOnly = _options.DeploymentOnly,
            BuildActive = _buildGateHeld,
            LastBuildAttemptAtUtc = lastBuildAttemptTicks == 0
                ? null
                : new DateTime(lastBuildAttemptTicks, DateTimeKind.Utc),
            BuildCacheAvailable = builder.Available,
            DockerInventoryAvailable = docker.ExitCode == 0,
            BuildCacheBytes = builder.Total,
            BuildCacheReclaimableBytes = builder.Reclaimable,
            DockerImagesBytes = images,
            DockerContainersBytes = containers,
            DockerVolumesBytes = volumes,
            AgentWorkDirectoryBytes = await GetPathSizeAsync(_agentOptions.WorkDirectory, ct).ConfigureAwait(false),
            AgentInstallDirectoryBytes = await GetPathSizeAsync(AppContext.BaseDirectory, ct).ConfigureAwait(false),
            NuGetCacheBytes = await GetPathSizeAsync(nugetPath, ct).ConfigureAwait(false),
            JournalBytes = await GetJournalSizeAsync(ct).ConfigureAwait(false),
            CollectedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(Math.Clamp(_options.MaintenanceIntervalMinutes, 5, 1440)), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await RunMaintenanceAsync("periodic", stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Periodic Docker storage maintenance failed");
            }
        }
    }

    internal static bool ShouldUseAggressivePolicy(
        int usedPercent, long freeBytes, DockerStorageMaintenanceOptions options) =>
        usedPercent >= options.PressureUsedPercent || freeBytes < GiB(options.MinFreeSpaceGiB);

    internal static string ResolveBuilderName(string? configured, string? agentName)
    {
        var source = string.IsNullOrWhiteSpace(configured)
            ? $"aetheus-{(string.IsNullOrWhiteSpace(agentName) ? Environment.MachineName : agentName)}"
            : configured;
        var normalized = Regex.Replace(source!.ToLowerInvariant(), "[^a-z0-9_.-]+", "-").Trim('-', '.');
        if (normalized.Length == 0) normalized = "aetheus-agent";
        return normalized.Length <= 63 ? normalized : normalized[..63].TrimEnd('-', '.');
    }

    internal static long ParseSizeBytes(string value)
    {
        var match = Regex.Match(value.Trim(), @"^(?<n>[0-9]+(?:[.,][0-9]+)?)\s*(?<u>[kmgtp](?:i?b)?|bytes?)?\*?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) return 0;
        var number = double.Parse(match.Groups["n"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var multiplier = match.Groups["u"].Value.ToUpperInvariant() switch
        {
            "K" or "KB" => 1_000d,
            "KIB" => 1_024d,
            "M" or "MB" => 1_000_000d,
            "MIB" => 1_048_576d,
            "G" or "GB" => 1_000_000_000d,
            "GIB" => 1_073_741_824d,
            "T" or "TB" => 1_000_000_000_000d,
            "TIB" => 1_099_511_627_776d,
            "P" or "PB" => 1_000_000_000_000_000d,
            "PIB" => 1_125_899_906_842_624d,
            _ => 1d
        };
        return checked((long)(number * multiplier));
    }

    private async Task EnsureBuilderAsync(CancellationToken ct)
    {
        var inspect = await shell.RunExecAsync(
            "docker", ["buildx", "inspect", BuilderName], ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (inspect.ExitCode == 0) return;
        if (IsDryRun)
        {
            logger.LogWarning("Buildx builder {Builder} is absent; dry-run will not create it", BuilderName);
            return;
        }

        var create = await shell.RunExecAsync(
            "docker", ["buildx", "create", "--name", BuilderName, "--driver", "docker-container"],
            ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (create.ExitCode != 0)
            throw new InvalidOperationException($"Could not create dedicated Buildx builder '{BuilderName}': {create.StdErr.Trim()}");
        logger.LogInformation("Created dedicated Buildx builder {Builder}", BuilderName);
    }

    private async Task RunMaintenanceUnderLockAsync(string reason, CancellationToken ct)
    {
        if (_options.DeploymentOnly && !_options.AllowBuildsOnDeploymentTarget)
        {
            logger.LogInformation(
                "Skipping Buildx cache maintenance ({Reason}): agent is deployment-only and builds are forbidden",
                reason);
            await RunNuGetMaintenanceUnderLockAsync(ct).ConfigureAwait(false);
            return;
        }

        var started = timeProvider.GetTimestamp();
        var before = await InspectBuilderBytesAsync(ct).ConfigureAwait(false);
        if (!before.Available && !IsDryRun)
            throw new InvalidOperationException($"Could not inventory dedicated Buildx builder '{BuilderName}'; refusing cache mutation.");
        var (usedPercent, freeBytes) = GetDiskState();
        var aggressive = ShouldUseAggressivePolicy(usedPercent, freeBytes, _options);
        var ageHours = aggressive ? _options.PressureCacheAgeHours : _options.MaxCacheAgeHours;

        logger.LogInformation(
            "Docker storage decision: builder={Builder}, reason={Reason}, dryRun={DryRun}, diskUsed={UsedPercent}%, " +
            "free={Free}, cache={Cache}, reclaimable={Reclaimable}, maxAge={AgeHours}h",
            BuilderName, reason, IsDryRun, usedPercent, FormatBytes(freeBytes),
            FormatBytes(before.Total), FormatBytes(before.Reclaimable), ageHours);

        if (!IsDryRun)
        {
            var prune = await shell.RunExecAsync("docker",
                ["buildx", "prune", "--builder", BuilderName, "--force", "--filter", $"until={ageHours}h",
                 "--reserved-space", $"{_options.ReservedSpaceGiB}GB", "--max-used-space", $"{_options.MaxCacheGiB}GB",
                 "--min-free-space", $"{_options.MinFreeSpaceGiB}GB"],
                ct, TimeSpan.FromMinutes(10)).ConfigureAwait(false);
            if (prune.ExitCode != 0)
                throw new InvalidOperationException($"Buildx prune failed for '{BuilderName}': {prune.StdErr.Trim()}");
        }

        var after = IsDryRun ? before : await InspectBuilderBytesAsync(ct).ConfigureAwait(false);
        await RunNuGetMaintenanceUnderLockAsync(ct).ConfigureAwait(false);
        logger.LogInformation(
            "Docker storage result: builder={Builder}, before={Before}, after={After}, reclaimed={Reclaimed}, durationMs={DurationMs}",
            BuilderName, FormatBytes(before.Total), FormatBytes(after.Total),
            FormatBytes(Math.Max(0, before.Total - after.Total)), timeProvider.GetElapsedTime(started).TotalMilliseconds);
    }

    private async Task<(bool Available, long Total, long Reclaimable)> InspectBuilderBytesAsync(CancellationToken ct)
    {
        var result = await shell.RunExecAsync(
            "docker", ["buildx", "du", "--builder", BuilderName], ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            logger.LogWarning("Could not inventory Buildx builder {Builder}: {Error}", BuilderName, result.StdErr.Trim());
            return (false, 0, 0);
        }

        long total = 0;
        long reclaimable = 0;
        foreach (var rawLine in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith("Total:", StringComparison.OrdinalIgnoreCase))
                total = ParseSizeBytes(rawLine["Total:".Length..]);
            else if (rawLine.StartsWith("Reclaimable:", StringComparison.OrdinalIgnoreCase))
                reclaimable = ParseSizeBytes(rawLine["Reclaimable:".Length..]);
        }
        return (true, total, reclaimable);
    }

    private async Task<long> GetPathSizeAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return 0;
        if (!OperatingSystem.IsWindows())
        {
            var result = await shell.RunExecAsync("du", ["-sb", "--", path], ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            // IShellRunner implementations return a result, but test doubles and a defensive
            // third-party implementation may still violate that contract. Diagnostics must
            // degrade to an unknown size instead of taking the whole agent heartbeat down.
            if (result is { ExitCode: 0 })
            {
                var token = result.StdOut.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes)) return bytes;
            }
            return 0;
        }

        try
        {
            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            long total = 0;
            var count = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", enumerationOptions))
            {
                ct.ThrowIfCancellationRequested();
                if (++count > MaxDiagnosticFiles)
                {
                    logger.LogWarning(
                        "Storage measurement for {Path} exceeded the {Limit} file safety limit",
                        path, MaxDiagnosticFiles);
                    return 0;
                }
                total = checked(total + new FileInfo(file).Length);
            }
            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            logger.LogWarning(ex, "Could not measure storage path {Path}", path);
            return 0;
        }
    }

    private async Task<long> GetJournalSizeAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) return 0;
        var result = await shell.RunExecAsync("journalctl", ["--disk-usage"], ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (result is not { ExitCode: 0 }) return 0;
        var match = Regex.Match(result.StdOut, @"(?<size>[0-9]+(?:[.,][0-9]+)?\s*[KMGTPE](?:i?B)?)\s+in\s+(?:the\s+)?file\s*system",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return match.Success ? ParseSizeBytes(match.Groups["size"].Value) : 0;
    }

    private Task RunNuGetMaintenanceUnderLockAsync(CancellationToken ct)
    {
        if (_options.NuGetCacheRetentionDays <= 0) return Task.CompletedTask;

        var path = ResolveNuGetPackagesPath();
        if (!TryResolveSafeNuGetRoot(path, out var root))
        {
            logger.LogError("NuGet cache retention refused: unsafe or unavailable packages root {Path}", path);
            return Task.CompletedTask;
        }

        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-_options.NuGetCacheRetentionDays);
        var candidates = new List<(DirectoryInfo Directory, long Bytes)>();
        try
        {
            foreach (var package in root.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                if (IsReparsePoint(package)) continue;

                var versions = package.EnumerateDirectories()
                    .Where(version => IsSafeNuGetVersionDirectory(version, root.FullName))
                    .OrderByDescending(version => version.LastWriteTimeUtc)
                    .ToList();
                foreach (var version in versions.Skip(1).Where(version => version.LastWriteTimeUtc < cutoff))
                    candidates.Add((version, MeasureDirectoryBytes(version, ct)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not inventory NuGet cache {Path}", path);
            return Task.CompletedTask;
        }

        var candidateBytes = candidates.Sum(candidate => candidate.Bytes);
        logger.LogInformation(
            "NuGet cache retention decision: path={Path}, dryRun={DryRun}, retentionDays={RetentionDays}, " +
            "candidateVersions={CandidateVersions}, candidateBytes={CandidateBytes}",
            path, IsDryRun, _options.NuGetCacheRetentionDays, candidates.Count, FormatBytes(candidateBytes));

        if (IsDryRun) return Task.CompletedTask;

        var removed = 0;
        long removedBytes = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!IsSafeNuGetVersionDirectory(candidate.Directory, root.FullName))
                {
                    logger.LogWarning(
                        "Skipping NuGet retention candidate that no longer satisfies the package-cache contract: {Path}",
                        candidate.Directory.FullName);
                    continue;
                }
                candidate.Directory.Delete(recursive: true);
                removed++;
                removedBytes += candidate.Bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not remove expired NuGet package version {Path}", candidate.Directory.FullName);
            }
        }

        logger.LogInformation(
            "NuGet cache retention result: removedVersions={RemovedVersions}, removedBytes={RemovedBytes}",
            removed, FormatBytes(removedBytes));
        return Task.CompletedTask;
    }

    private string ResolveNuGetPackagesPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.NuGetPackagesPath)) return _options.NuGetPackagesPath;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(userProfile, ".nuget", "packages");
    }

    private static bool IsReparsePoint(FileSystemInfo entry) =>
        (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    private static bool TryResolveSafeNuGetRoot(string path, out DirectoryInfo root)
    {
        root = null!;
        try
        {
            var fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var volumeRoot = Path.GetPathRoot(fullPath)?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(fullPath)
                || string.Equals(fullPath, volumeRoot, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal)
                || !Directory.Exists(fullPath))
            {
                return false;
            }

            root = new DirectoryInfo(fullPath);
            root.Refresh();
            return !IsReparsePoint(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSafeNuGetVersionDirectory(DirectoryInfo version, string rootPath)
    {
        try
        {
            version.Refresh();
            if (!version.Exists || IsReparsePoint(version) || version.Parent?.Parent is null) return false;

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(
                    Path.GetFullPath(version.Parent.Parent.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    comparison))
            {
                return false;
            }

            var metadata = new FileInfo(Path.Combine(version.FullName, ".nupkg.metadata"));
            metadata.Refresh();
            return metadata.Exists && !IsReparsePoint(metadata);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static long MeasureDirectoryBytes(DirectoryInfo directory, CancellationToken ct)
    {
        try
        {
            long total = 0;
            foreach (var file in directory.EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            }))
            {
                ct.ThrowIfCancellationRequested();
                total = checked(total + file.Length);
            }
            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            return 0;
        }
    }

    private static (int UsedPercent, long FreeBytes) GetDiskState()
    {
        var root = OperatingSystem.IsWindows()
            ? Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"
            : "/";
        var drive = new DriveInfo(root);
        var usedPercent = drive.TotalSize <= 0
            ? 0
            : (int)Math.Round(100d * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize);
        return (usedPercent, drive.AvailableFreeSpace);
    }

    private void ReleaseBuildGate()
    {
        if (!_buildGateHeld) return;
        _buildGateHeld = false;
        _exclusiveGate.Release();
    }

    private static long GiB(int value) => checked((long)value * 1024 * 1024 * 1024);

    private static string FormatBytes(long bytes) =>
        bytes >= GiB(1)
            ? $"{bytes / (double)GiB(1):0.##} GiB"
            : $"{bytes / (1024d * 1024d):0.##} MiB";
}
