// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// F-32 typed operation: <see cref="OperationKind.AgentSelfUpdate"/>.
///
/// The agent cannot overwrite its own running binaries, so the update is a two-phase handoff:
///  1. Download the latest agent archive from the backend's anonymous <c>/downloads</c> endpoint
///     (URL derived from the agent's own configured <c>ServerUrl</c>) and unpack it to a staging
///     directory inside the work dir.
///  2. Write a small platform updater script and launch it <em>detached</em> so it outlives this
///     process. After the task result reaches the backend, the polling service exits with a
///     failure code. The updater waits for the agent process to exit, swaps
///     the binaries in the install directory (preserving <c>appsettings.json</c> and the work dir),
///     and the service manager's auto-restart policy (systemd <c>Restart=always</c> /
///     Windows SCM failure-restart) brings the new build back up. The freshly started agent
///     re-enrolls from its persisted credentials and reports the new version on its next heartbeat.
///
/// No privilege escalation is required: the agent already has write access to its install
/// directory (Linux systemd unit grants <c>ReadWritePaths=$INSTALL_DIR</c>; the Windows service
/// account owns its program directory) and the updater never calls into <c>systemctl</c>/<c>sc</c>.
/// </summary>
public sealed class AgentSelfUpdateOperationExecutor(
    IHttpClientFactory httpClientFactory,
    IOptions<AetheusAgentOptions> options,
    IServerApiClient serverApi,
    AgentState agentState,
    ILogger<AgentSelfUpdateOperationExecutor> logger) : IOperationExecutor
{
    private readonly AetheusAgentOptions _options = options.Value;

    private const string LinuxArchiveName = "aetheus-agent-linux-x64.tar.gz";
    private const string WindowsArchiveName = "aetheus-agent-win-x64.zip";
    internal const long MaxArchiveBytes = 256L * 1024 * 1024;
    internal const long MaxExtractedPayloadBytes = 1024L * 1024 * 1024;
    internal const int MaxArchiveEntries = 4096;

    public bool CanHandle(OperationKind kind) => kind is OperationKind.AgentSelfUpdate;

    /// <summary>
    /// Fire-and-forget phase emitter. Reports to the backend so UI clients on the server group
    /// can render the progress bar (item #3 of the plan). Failures are swallowed inside
    /// <see cref="IServerApiClient.ReportUpdateProgressAsync"/> - a self-update must never abort
    /// because a telemetry beat couldn't go through.
    /// </summary>
    private async Task ReportPhaseAsync(AgentUpdatePhase phase, int percent, string? message, CancellationToken ct)
    {
        if (agentState.ServerId is not { } serverId) return; // not enrolled yet (defensive)
        await serverApi.ReportUpdateProgressAsync(
            serverId,
            new AgentUpdateProgressReport { Phase = phase, Percent = percent, Message = message },
            ct).ConfigureAwait(false);
    }

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        if (kind != OperationKind.AgentSelfUpdate)
            return new ExecutorResult(-1, false);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var stagingRoot = Path.Combine(_options.WorkDirectory, ".agent-update");
        var rollbackRoot = Path.Combine(_options.WorkDirectory, ".agent-rollback");

        try
        {
            if (AgentSelfUpdateFileSystem.IsSameOrChildPath(_options.WorkDirectory, installDir))
            {
                await onOutput(
                    "Self-update refused: WorkDirectory must be outside the agent installation directory.",
                    TaskLogLevel.Error).ConfigureAwait(false);
                return new ExecutorResult(-1, false);
            }

            await ReportPhaseAsync(AgentUpdatePhase.PickedUp, 10, null, cancellationToken).ConfigureAwait(false);
            await onOutput($"Starting agent self-update (install dir: {installDir})", TaskLogLevel.Info).ConfigureAwait(false);

            // --- Phase 1: download + extract -------------------------------------------------
            AgentSelfUpdateFileSystem.PrepareCleanDirectory(stagingRoot);
            var archivePath = Path.Combine(stagingRoot, isWindows ? WindowsArchiveName : LinuxArchiveName);
            var extractDir = Path.Combine(stagingRoot, "new");
            Directory.CreateDirectory(extractDir);

            var downloadUrl = $"{_options.ServerUrl.TrimEnd('/')}/downloads/{(isWindows ? WindowsArchiveName : LinuxArchiveName)}";
            await ReportPhaseAsync(AgentUpdatePhase.Downloading, 20, downloadUrl, cancellationToken).ConfigureAwait(false);
            await onOutput($"Downloading latest agent from {downloadUrl}", TaskLogLevel.Info).ConfigureAwait(false);

            if (!await DownloadAsync(downloadUrl, archivePath, onOutput, cancellationToken).ConfigureAwait(false))
            {
                await ReportPhaseAsync(AgentUpdatePhase.Failed, 20, "download failed", cancellationToken).ConfigureAwait(false);
                return new ExecutorResult(-1, false);
            }
            await ReportPhaseAsync(AgentUpdatePhase.Downloaded, 50, null, cancellationToken).ConfigureAwait(false);

            await ReportPhaseAsync(AgentUpdatePhase.Extracting, 60, null, cancellationToken).ConfigureAwait(false);
            await onOutput("Extracting update package…", TaskLogLevel.Info).ConfigureAwait(false);
            if (!await ExtractAsync(archivePath, extractDir, isWindows, onOutput, cancellationToken).ConfigureAwait(false))
            {
                await ReportPhaseAsync(AgentUpdatePhase.Failed, 60, "extraction failed", cancellationToken).ConfigureAwait(false);
                return new ExecutorResult(-1, false);
            }

            // Guard: refuse to proceed if the archive is missing the main agent binary.
            if (!StagedPayloadLooksValid(extractDir, isWindows))
            {
                await ReportPhaseAsync(AgentUpdatePhase.Failed, 70, "staged payload invalid", cancellationToken).ConfigureAwait(false);
                await onOutput("Update package does not contain the expected agent binaries - aborting.", TaskLogLevel.Error).ConfigureAwait(false);
                return new ExecutorResult(-1, false);
            }

            // --- Phase 2: swap binaries + request shutdown --------------------------------
            await ReportPhaseAsync(AgentUpdatePhase.LaunchingUpdater, 80, null, cancellationToken).ConfigureAwait(false);

            if (isWindows)
            {
                // Windows locks running EXEs - must use a detached updater script.
                var updaterPath = WriteWindowsUpdater(stagingRoot);
                await onOutput("Launching detached updater...", TaskLogLevel.Info).ConfigureAwait(false);
                LaunchDetached(updaterPath, isWindows, logger,
                    [Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), extractDir, installDir]);
                // Honest status: at this point the task reports success of the HANDOFF only -
                // the binary swap happens after this process exits, and its outcome is
                // confirmed by the version reported on the next heartbeat.
                await onOutput("Update handed off to the detached updater. The swap outcome is confirmed by the agent version on the next heartbeat.", TaskLogLevel.Warning).ConfigureAwait(false);
            }
            else
            {
                // Linux: replace files in-place. Running processes keep their mmapped pages -
                // the on-disk swap is safe while the process is alive. Preserve appsettings.json.
                await onOutput("Applying update in-place...", TaskLogLevel.Info).ConfigureAwait(false);
                AgentSelfUpdateFileSystem.SnapshotRollback(installDir, rollbackRoot);
                await onOutput($"Rollback snapshot created at {rollbackRoot}", TaskLogLevel.Info).ConfigureAwait(false);
                AgentSelfUpdateFileSystem.ReplaceInstallFilesWithRollback(extractDir, installDir, rollbackRoot);
                await onOutput("Binaries swapped. Restarting...", TaskLogLevel.Info).ConfigureAwait(false);
            }

            return new ExecutorResult(0, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SafeOutput(onOutput, "Self-update cancelled.", TaskLogLevel.Warning).ConfigureAwait(false);
            return new ExecutorResult(-1, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent self-update failed");
            // Best-effort failure beat. The CancellationToken may already be tripped if the
            // exception came from a downstream operation; pass CancellationToken.None so the
            // beat can still go through during teardown.
            await SafeReportAsync(AgentUpdatePhase.Failed, 0, ex.Message).ConfigureAwait(false);
            await SafeOutput(onOutput, $"Self-update failed: {ex.Message}", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }
    }

    private async Task SafeReportAsync(AgentUpdatePhase phase, int percent, string? message)
    {
        try { await ReportPhaseAsync(phase, percent, message, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception) { /* logging best-effort during teardown */ }
    }

    private async Task<bool> DownloadAsync(
        string url, string destination, Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        // The /downloads endpoint is anonymous; a plain client (no bearer handler) is enough.
        // Threat model: the X-Content-SHA256 header below is emitted by the same origin that
        // serves the archive, so integrity reduces to "trust TLS + trust the backend". The
        // Linux archive is re-packed per request (server-URL marker), so its hash cannot be
        // pre-registered out-of-band. Against a MITM holding a trusted cert, configure
        // Aetheus:PinnedServerCertThumbprint - the transfer client's TLS handler enforces
        // the pin for this download. A compromised backend remains out of scope (it could
        // sign whatever it serves).
        using var client = httpClientFactory.CreateClient("AetheusServerTransfer");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await onOutput($"Download failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}", TaskLogLevel.Error).ConfigureAwait(false);
            return false;
        }

        // F-004: integrity check is FAIL-CLOSED. The backend always emits X-Content-SHA256 on
        // the agent download endpoints, so a missing header means tampering or a hostile mirror
        // - not a legitimate response. Single escape hatch: AllowInsecureCerts=true (dev mode
        // against an older backend) tolerates a missing header, logged as a warning.
        var expected = response.Headers.TryGetValues("X-Content-SHA256", out var checksumValues)
            ? checksumValues.FirstOrDefault()
            : null;
        if (string.IsNullOrEmpty(expected))
        {
            if (_options.AllowInsecureCerts)
            {
                logger.LogWarning("Update archive served without X-Content-SHA256 - accepted only because AllowInsecureCerts=true (dev mode)");
                await onOutput("WARNING: no X-Content-SHA256 header - accepted because AllowInsecureCerts=true (dev mode)", TaskLogLevel.Warning).ConfigureAwait(false);
            }
            else
            {
                logger.LogError("Agent self-update rejected: missing X-Content-SHA256 header");
                await onOutput("update rejected: missing X-Content-SHA256", TaskLogLevel.Error).ConfigureAwait(false);
                return false;
            }
        }

        if (response.Content.Headers.ContentLength is > MaxArchiveBytes)
        {
            await onOutput(
                $"Update archive exceeds the {MaxArchiveBytes / (1024 * 1024)} MiB safety limit - aborting.",
                TaskLogLevel.Error).ConfigureAwait(false);
            return false;
        }

        long sizeBytes = 0;
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var exceededLimit = false;
        await using (var output = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            while (true)
            {
                var read = await input.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0) break;

                sizeBytes = checked(sizeBytes + read);
                if (sizeBytes > MaxArchiveBytes)
                {
                    exceededLimit = true;
                    break;
                }

                hasher.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }

        if (exceededLimit)
        {
            File.Delete(destination);
            await onOutput(
                $"Update archive exceeds the {MaxArchiveBytes / (1024 * 1024)} MiB safety limit - aborting.",
                TaskLogLevel.Error).ConfigureAwait(false);
            return false;
        }

        if (sizeBytes == 0)
        {
            File.Delete(destination);
            await onOutput("Downloaded archive is empty - aborting.", TaskLogLevel.Error).ConfigureAwait(false);
            return false;
        }

        await onOutput($"Downloaded {sizeBytes / 1024} KB", TaskLogLevel.Info).ConfigureAwait(false);

        var actual = Convert.ToHexString(hasher.GetHashAndReset());
        if (expected is not null && !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Agent self-update rejected: invalid X-Content-SHA256 (expected {Expected}, got {Actual})", expected, actual);
            await onOutput($"update rejected: invalid X-Content-SHA256 (expected {expected}, got {actual})", TaskLogLevel.Error).ConfigureAwait(false);
            return false;
        }
        if (expected is not null)
            await onOutput("SHA256 checksum verified", TaskLogLevel.Info).ConfigureAwait(false);

        return true;
    }

    private static async Task<bool> ExtractAsync(
        string archivePath, string extractDir, bool isWindows,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        if (!ValidateArchive(archivePath, extractDir, isWindows, out var validationError))
        {
            await onOutput($"Update archive rejected: {validationError}", TaskLogLevel.Error).ConfigureAwait(false);
            return false;
        }

        if (isWindows)
        {
            // No native zip CLI guaranteed on Windows - use the framework extractor.
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true), ct).ConfigureAwait(false);
            return true;
        }

        // tar is universally present on Linux and preserves the executable bit, which
        // ZipFile/TarFile-managed extraction to a Unix FS would otherwise drop for the host.
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-xzf");
        psi.ArgumentList.Add(archivePath);
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(extractDir);

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            await onOutput($"tar extraction failed (exit {process.ExitCode}): {stderr}", TaskLogLevel.Error).ConfigureAwait(false);
            return false;
        }
        return true;
    }

    internal static bool ValidateArchive(
        string archivePath, string extractDir, bool isWindows, out string validationError)
    {
        try
        {
            long totalBytes = 0;
            var entries = 0;
            if (isWindows)
            {
                using var archive = ZipFile.OpenRead(archivePath);
                foreach (var entry in archive.Entries)
                {
                    ValidateArchiveEntry(entry.FullName, extractDir);
                    if (IsZipSymbolicLink(entry))
                        throw new InvalidDataException($"symbolic link entry is forbidden: {entry.FullName}");
                    AddArchiveEntryBudget(entry.Length, ref totalBytes, ref entries);
                }
            }
            else
            {
                using var archive = File.OpenRead(archivePath);
                using var gzip = new GZipStream(archive, CompressionMode.Decompress);
                using var reader = new TarReader(gzip);
                TarEntry? entry;
                while ((entry = reader.GetNextEntry(copyData: false)) is not null)
                {
                    ValidateArchiveEntry(entry.Name, extractDir);
                    if (entry.EntryType is not (TarEntryType.Directory or TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                        throw new InvalidDataException($"unsupported tar entry type {entry.EntryType}: {entry.Name}");
                    AddArchiveEntryBudget(entry.Length, ref totalBytes, ref entries);
                }
            }

            if (entries == 0) throw new InvalidDataException("archive contains no entries");
            validationError = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException)
        {
            validationError = ex.Message;
            return false;
        }
    }

    private static void AddArchiveEntryBudget(long length, ref long totalBytes, ref int entries)
    {
        if (length < 0) throw new InvalidDataException("archive entry declares a negative length");
        entries = checked(entries + 1);
        if (entries > MaxArchiveEntries)
            throw new InvalidDataException($"archive contains more than {MaxArchiveEntries} entries");

        totalBytes = checked(totalBytes + length);
        if (totalBytes > MaxExtractedPayloadBytes)
            throw new InvalidDataException(
                $"expanded payload exceeds {MaxExtractedPayloadBytes / (1024 * 1024)} MiB");
    }

    private static void ValidateArchiveEntry(string name, string extractDir)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOf('\0') >= 0)
            throw new InvalidDataException("archive contains an invalid empty entry name");

        var normalized = name.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            throw new InvalidDataException($"absolute archive path is forbidden: {name}");

        var root = Path.GetFullPath(extractDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, normalized));
        if (!destination.StartsWith(root, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            throw new InvalidDataException($"archive path escapes the staging directory: {name}");
        }
    }

    private static bool IsZipSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static bool StagedPayloadLooksValid(string extractDir, bool isWindows)
    {
        var marker = isWindows ? "Aetheus.Agent.Windows.exe" : "Aetheus.Agent.Linux.dll";
        return File.Exists(Path.Combine(extractDir, marker));
    }

    // --- Detached updater script (Windows only) ----------------------------------------------
    // Linux applies the update in-place (see ExecuteAsync); Windows locks running EXEs so it
    // needs a detached script: wait for THIS process to exit, mirror staged files into the
    // install dir (preserving appsettings.json), then exit. The Windows SCM failure-restart
    // policy brings the new build back.

    internal static string WriteWindowsUpdater(string stagingRoot)
    {
        var scriptPath = Path.Combine(stagingRoot, "apply-update.ps1");
        // The script is a constant: pid/src/dest arrive as bound -File parameters, never
        // interpolated into a Bypass-policy script body.
        const string script = """
            # Aetheus agent self-update applier (generated, ephemeral).
            param([int]$AgentPid, [string]$Src, [string]$Dest)
            $ErrorActionPreference = 'Stop'

            # Wait for the agent process to exit (max ~30s) so the EXE/DLLs are unlocked.
            for ($i = 0; $i -lt 60; $i++) {
                if (-not (Get-Process -Id $AgentPid -ErrorAction SilentlyContinue)) { break }
                Start-Sleep -Milliseconds 500
            }
            if (Get-Process -Id $AgentPid -ErrorAction SilentlyContinue) {
                Add-Content -LiteralPath (Join-Path (Split-Path $Src -Parent) 'update.log') `
                    -Value "Update refused: agent process $AgentPid did not stop within 30 seconds."
                exit 3
            }

            # Keep one bounded rollback snapshot and restore it automatically if any copy fails.
            $StagingRoot = Split-Path $Src -Parent
            $WorkRoot = Split-Path $StagingRoot -Parent
            $Rollback = Join-Path $WorkRoot '.agent-rollback'
            $RollbackReady = $false
            try {
                Remove-Item -LiteralPath $Rollback -Recurse -Force -ErrorAction SilentlyContinue
                New-Item -ItemType Directory -Path $Rollback -Force | Out-Null
                $UnsafeLink = Get-ChildItem -LiteralPath $Dest -Recurse -Force | Where-Object {
                    $_.Attributes -band [IO.FileAttributes]::ReparsePoint
                } | Select-Object -First 1
                if ($UnsafeLink) {
                    throw "Install directory contains a reparse point: $($UnsafeLink.FullName)"
                }
                Get-ChildItem -LiteralPath $Dest -Force | Where-Object {
                    $_.Name -ne 'appsettings.json' -and -not $_.Name.EndsWith('.bak')
                } | Copy-Item -Destination $Rollback -Recurse -Force
                $ExistingAgent = Join-Path $Dest 'Aetheus.Agent.Windows.exe'
                if (Test-Path -LiteralPath $ExistingAgent) {
                    (Get-Item -LiteralPath $ExistingAgent).VersionInfo.FileVersion |
                        Set-Content -LiteralPath (Join-Path $Rollback 'rollback-version.txt')
                }
                $RollbackReady = $true

                # Mirror new binaries, preserving the operator's appsettings.json.
                Remove-Item -Path (Join-Path $Src 'appsettings.json') -ErrorAction SilentlyContinue
                Get-ChildItem -LiteralPath $Dest -Force | Where-Object {
                    $_.Name -ne 'appsettings.json'
                } | Remove-Item -Recurse -Force
                Copy-Item -Path (Join-Path $Src '*') -Destination $Dest -Recurse -Force
                exit 0
            }
            catch {
                $UpdateError = $_.Exception.Message
                if ($RollbackReady) {
                    try {
                        Get-ChildItem -LiteralPath $Dest -Force | Where-Object {
                            $_.Name -ne 'appsettings.json'
                        } | Remove-Item -Recurse -Force
                        Get-ChildItem -LiteralPath $Rollback -Force | Where-Object {
                            $_.Name -ne 'rollback-version.txt'
                        } | Copy-Item -Destination $Dest -Recurse -Force
                    }
                    catch {
                        Add-Content -LiteralPath (Join-Path $StagingRoot 'update.log') `
                            -Value "Update failed: $UpdateError; automatic rollback also failed: $($_.Exception.Message)"
                        exit 2
                    }
                    Add-Content -LiteralPath (Join-Path $StagingRoot 'update.log') `
                        -Value "Update failed: $UpdateError; previous binaries restored automatically."
                    exit 1
                }
                Add-Content -LiteralPath (Join-Path $StagingRoot 'update.log') `
                    -Value "Update failed before replacement started: $UpdateError"
                exit 1
            }
            """;
        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    private static void LaunchDetached(string scriptPath, bool isWindows, ILogger logger, IReadOnlyList<string>? scriptArgs = null)
    {
        ProcessStartInfo psi;
        if (isWindows)
        {
            psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(scriptPath);
            foreach (var arg in scriptArgs ?? [])
                psi.ArgumentList.Add(arg);
        }
        else
        {
            // systemd KillMode=control-group kills all processes in the cgroup on service stop.
            // setsid/nohup cannot escape the cgroup. Use `at now` to schedule the updater via
            // atd (runs outside the service cgroup entirely). Falls back to setsid.
            var atPath = File.Exists("/usr/bin/at") ? "/usr/bin/at" : File.Exists("/bin/at") ? "/bin/at" : null;
            if (atPath is not null)
            {
                psi = new ProcessStartInfo
                {
                    FileName = atPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("now");
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = "setsid",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("sh");
                psi.ArgumentList.Add(scriptPath);
            }
        }

        var child = Process.Start(psi);
        if (child is null)
        {
            throw new InvalidOperationException("Failed to launch detached self-update helper");
        }
        else
        {
            if (child.StartInfo.RedirectStandardInput && psi.FileName.EndsWith("at", StringComparison.Ordinal))
            {
                // `at` reads a shell command list from stdin (that IS its interface - the job cannot be
                // passed as argv), so a shell line is unavoidable here. Single-quote the agent-generated
                // script path so it can neither word-split nor glob, and write the update log to an
                // owner-controlled path under the staging dir instead of world-readable /tmp.
                // (This Linux branch is currently unreachable - LaunchDetached is only invoked for Windows,
                // Linux updates in-place - but is hardened in case the detached path is ever revived.)
                var logPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "update.log");
                child.StandardInput.WriteLine($"sh '{scriptPath}' > '{logPath}' 2>&1");
                child.StandardInput.Close();
            }
            logger.LogInformation("Detached self-update helper started (pid {Pid})", child.Id);
        }
    }

    private static async Task SafeOutput(Func<string, TaskLogLevel, Task> onOutput, string message, TaskLogLevel level)
    {
        try { await onOutput(message, level).ConfigureAwait(false); }
        catch (Exception) { /* logging best-effort during teardown */ }
    }
}
