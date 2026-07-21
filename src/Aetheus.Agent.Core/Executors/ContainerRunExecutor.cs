// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Runtime.InteropServices;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Executors;

/// <summary>
/// Phase 2 isolation: runs a step's shell script inside a fresh, hardened, single-use container
/// (<c>docker run --rm</c>). The boundary here is the container, not a command allow-list - so the
/// script is delivered verbatim, but the container drops every Linux capability, forbids privilege
/// escalation, caps its process count, and is destroyed as soon as the step finishes.
/// <para>
/// The run's source lives in a per-run host directory (<c>{WorkDir}/cw/{runId}</c>) bind-mounted at
/// <c>/w</c>, so every step of a run shares the cloned tree while staying isolated from other runs.
/// The script is written into that mounted directory (no shell-quoting of the user script) and run
/// as <c>/w/.prom-step-*.sh</c>. The container runtime is pluggable (<c>runc</c> default, <c>runsc</c>
/// for gVisor, <c>kata</c> for micro-VMs) so kernel-level isolation can be switched on without code.
/// </para>
/// </summary>
public sealed class ContainerRunExecutor(
    IOptions<AetheusAgentOptions> options,
    ILogger<ContainerRunExecutor> logger) : IContainerExecutor
{
    private readonly AetheusAgentOptions _options = options.Value;

    private const int PidsLimit = 4096;

    // S-TECH-72: the agent's real uid/gid, passed to `docker run --user` so container output is
    // owned by the agent rather than root. Linux-only (container isolation is a Linux feature).
    [DllImport("libc", SetLastError = false)]
    private static extern uint getuid();

    [DllImport("libc", SetLastError = false)]
    private static extern uint getgid();

    // Closed allow-list of OCI runtimes that may reach `docker run --runtime`. `runc` is the
    // implicit default (never passed explicitly); only the kernel-isolating runtimes are accepted.
    // Defense-in-depth: docker already rejects an unregistered runtime, but a verbatim arbitrary
    // value would otherwise undermine the isolation guarantee this executor exists to provide.
    private static readonly HashSet<string> AllowedRuntimes =
        new(StringComparer.OrdinalIgnoreCase) { "runc", "runsc", "kata" };

    public async Task<ExecutorResult> ExecuteAsync(
        ContainerSpec spec,
        string command,
        Dictionary<string, string> environmentVariables,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(spec.Image))
        {
            await onOutput("Container step has no image - blocked.", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        if (!string.IsNullOrWhiteSpace(spec.Runtime) && !AllowedRuntimes.Contains(spec.Runtime))
        {
            await onOutput($"Container runtime '{spec.Runtime}' is not allowed (expected one of: runc, runsc, kata) - blocked.", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        timeoutSeconds = Math.Clamp(timeoutSeconds, _options.MinTimeoutSeconds, _options.MaxTimeoutSeconds);

        // Per-run host workspace, owner-only. Shared across the run's steps so the clone persists.
        var hostWorkspace = Path.Combine(_options.WorkDirectory, "cw", spec.WorkspaceKey.ToString());
        Directory.CreateDirectory(hostWorkspace);
        TrySetOwnerOnly(hostWorkspace);

        var scriptName = $".prom-step-{Guid.NewGuid():N}.sh";
        var scriptPath = Path.Combine(hostWorkspace, scriptName);
        await File.WriteAllTextAsync(scriptPath, command, cancellationToken).ConfigureAwait(false);

        var containerName = $"prom-{Guid.NewGuid():N}";
        // Decide the secret file paths SYNCHRONOUSLY (no I/O) so they are known before the try/finally.
        // The actual writes happen INSIDE the try below, so a mid-write cancellation leaves the partial
        // secret-bearing files to the finally's cleanup rather than leaking them.
        // Single-line values -> owner-only --env-file. S-TECH-Q9MF: multi-line values (e.g. PEM keys) that
        // the env-file format cannot represent go through an owner-only shell file bind-mounted read-only
        // and sourced by the container - NOT the former inline `-e KEY=VALUE`, which stays visible in
        // /proc/<pid>/cmdline for the duration of the run.
        var representableEnv = environmentVariables.Where(kv => IsEnvFileRepresentable(kv.Key, kv.Value)).ToList();
        var mountedSecrets = environmentVariables
            .Where(kv => !IsEnvFileRepresentable(kv.Key, kv.Value) && IsValidShellKey(kv.Key)).ToList();
        foreach (var (key, _) in environmentVariables.Where(kv => !IsEnvFileRepresentable(kv.Key, kv.Value) && !IsValidShellKey(kv.Key)))
            logger.LogWarning("Dropping container env var '{Key}': not a valid shell identifier, cannot be delivered securely", key);

        var envFilePath = representableEnv.Count > 0 ? Path.Combine(Path.GetTempPath(), $"{containerName}.env") : null;
        var secretsFilePath = mountedSecrets.Count > 0 ? Path.Combine(Path.GetTempPath(), $"{containerName}.secrets.sh") : null;
        var psi = BuildDockerRun(spec, hostWorkspace, scriptName, containerName, envFilePath, secretsFilePath);

        logger.LogDebug("Container run: image={Image} runtime={Runtime} network={Network} (timeout={Timeout}s)",
            spec.Image, spec.Runtime ?? "runc", NetworkMode(spec.Network), timeoutSeconds);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            if (envFilePath is not null)
                await WriteEnvFileLinesAsync(representableEnv, envFilePath, cancellationToken).ConfigureAwait(false);
            if (secretsFilePath is not null)
                await WriteSecretsFileLinesAsync(mountedSecrets, secretsFilePath, cancellationToken).ConfigureAwait(false);

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = ExecutorHelper.StreamOutputAsync(process.StandardOutput, TaskLogLevel.Info, onOutput, timeoutCts.Token, logger);
            var stderrTask = ExecutorHelper.StreamOutputAsync(process.StandardError, TaskLogLevel.Error, onOutput, timeoutCts.Token, logger);

            try
            {
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                return new ExecutorResult(process.ExitCode, false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Container step timed out after {Timeout}s - removing container {Name}", timeoutSeconds, containerName);
                await ForceRemoveContainerAsync(containerName).ConfigureAwait(false);
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) { logger.LogDebug(ex, "Kill after timeout failed"); }
                return new ExecutorResult(-1, true);
            }
        }
        finally
        {
            // --rm handles the normal path; force-remove covers a killed client / cancellation so a
            // detached container can't linger.
            if (cancellationToken.IsCancellationRequested)
                await ForceRemoveContainerAsync(containerName).ConfigureAwait(false);
            try { File.Delete(scriptPath); } catch (Exception ex) { logger.LogDebug(ex, "Step script cleanup failed"); }
            if (envFilePath is not null)
            {
                try { File.Delete(envFilePath); } catch (Exception ex) { logger.LogDebug(ex, "Env file cleanup failed"); }
            }
            if (secretsFilePath is not null)
            {
                try { File.Delete(secretsFilePath); } catch (Exception ex) { logger.LogDebug(ex, "Secrets file cleanup failed"); }
            }

            // Run cleanup step: remove the per-run host workspace dir. The in-container `rm -rf /w`
            // only empties the bind mount - the host mount-point dir would otherwise leak per run.
            if (spec.PurgeWorkspace)
            {
                try
                {
                    if (Directory.Exists(hostWorkspace))
                        Directory.Delete(hostWorkspace, recursive: true);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Workspace purge failed for {Workspace}", hostWorkspace);
                }
            }
        }
    }

    // Fixed in-container path the multi-line-secret shell file is bind-mounted at and sourced from.
    private const string ContainerSecretsPath = "/run/aetheus/.step-secrets.sh";

    private ProcessStartInfo BuildDockerRun(
        ContainerSpec spec, string hostWorkspace, string scriptName, string containerName, string? envFilePath, string? secretsFilePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var a = psi.ArgumentList;
        a.Add("run");
        a.Add("--rm");
        a.Add("--name"); a.Add(containerName);
        // Hardening: no Linux capabilities, no privilege escalation, bounded process count.
        a.Add("--cap-drop"); a.Add("ALL");
        a.Add("--security-opt"); a.Add("no-new-privileges");
        a.Add("--pids-limit"); a.Add(PidsLimit.ToString());
        // S-UX-35: optional per-run resource ceilings (validated server-side before reaching here).
        if (!string.IsNullOrWhiteSpace(spec.Memory)) { a.Add("--memory"); a.Add(spec.Memory); }
        if (!string.IsNullOrWhiteSpace(spec.Cpus)) { a.Add("--cpus"); a.Add(spec.Cpus); }
        // S-TECH-72: run as the agent's own uid:gid so files written into the bind-mounted host
        // workspace are owned by the unprivileged agent user (not root). A non-root agent cannot
        // chown root-owned files back, so without this a later non-container step of the same run
        // would hit permission errors on the shared workspace.
        if (!OperatingSystem.IsWindows())
        {
            a.Add("--user"); a.Add($"{getuid()}:{getgid()}");
        }
        a.Add("--network"); a.Add(NetworkMode(spec.Network));
        if (!string.IsNullOrWhiteSpace(spec.Runtime) && !string.Equals(spec.Runtime, "runc", StringComparison.OrdinalIgnoreCase))
        {
            a.Add("--runtime"); a.Add(spec.Runtime);
        }
        // Secrets stay off the docker CLI's argv (/proc/<pid>/cmdline is world-readable while the client
        // runs): single-line values travel via the owner-only env-file; multi-line values (S-TECH-Q9MF)
        // via an owner-only shell file bind-mounted read-only and sourced by the container. Neither ever
        // appears on argv.
        if (envFilePath is not null)
        {
            a.Add("--env-file"); a.Add(envFilePath);
        }
        if (secretsFilePath is not null)
        {
            a.Add("-v"); a.Add($"{secretsFilePath}:{ContainerSecretsPath}:ro");
        }
        a.Add("-v"); a.Add($"{hostWorkspace}:/w");
        a.Add("-w"); a.Add("/w");
        // Override the image's entrypoint so the step script runs regardless of the image default.
        a.Add("--entrypoint"); a.Add("bash");
        // End-of-options marker: everything after is positional (image, then command). Without it an
        // image reference starting with '-' (e.g. "--privileged") would be parsed by docker as a flag,
        // bypassing the hardening above. The script path is a fixed, server-controlled value.
        a.Add("--");
        a.Add(spec.Image);
        if (secretsFilePath is not null)
        {
            // Source the mounted secrets (exports the multi-line vars), then exec the step script so the
            // secrets propagate into it. The secrets path is a fixed constant; scriptName is agent-generated
            // and MUST stay a shell-safe token (see scriptName = $".prom-step-{Guid.NewGuid():N}.sh" above -
            // alphanumeric only): it is interpolated unquoted into this -c command, so any format change that
            // introduced a space/metacharacter would break the invariant.
            a.Add("-c"); a.Add($". {ContainerSecretsPath}; exec bash /w/{scriptName}");
        }
        else
        {
            a.Add($"/w/{scriptName}");
        }
        return psi;
    }

    // The docker env-file format is line-based: multi-line values (e.g. PEM secrets) cannot be
    // represented and a key containing '=' or a newline would corrupt neighbouring entries.
    private static bool IsEnvFileRepresentable(string key, string value) =>
        !key.Contains('=') && !key.Contains('\n') && !key.Contains('\r')
        && !value.Contains('\n') && !value.Contains('\r');

    // A POSIX shell variable name (so it can be `export`ed from the sourced secrets file): a letter or
    // underscore followed by letters, digits, or underscores. Guards against injection via the key.
    internal static bool IsValidShellKey(string key) =>
        key.Length > 0
        && (char.IsAsciiLetter(key[0]) || key[0] == '_')
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    // Writes the multi-line secrets to an owner-only (0600) shell file as `export KEY='value'` lines,
    // single-quote-escaped so any character in the value (newlines, quotes) survives verbatim. The caller
    // owns the path decision + the finally cleanup. The file is bind-mounted read-only and sourced.
    internal static async Task WriteSecretsFileLinesAsync(
        IReadOnlyList<KeyValuePair<string, string>> secrets, string path, CancellationToken ct)
    {
        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
            streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        await using var stream = new FileStream(path, streamOptions);
        await using var writer = new StreamWriter(stream);
        foreach (var (key, value) in secrets)
        {
            var escaped = value.Replace("'", "'\\''", StringComparison.Ordinal);
            await writer.WriteLineAsync($"export {key}='{escaped}'".AsMemory(), ct).ConfigureAwait(false);
        }
    }

    // Writes the representable env vars to an owner-only (0600) env-file at the given path. The
    // caller owns the path decision + the finally cleanup, so a throw here leaves a partial file
    // to be deleted rather than leaked.
    private static async Task WriteEnvFileLinesAsync(
        IReadOnlyList<KeyValuePair<string, string>> representable, string path, CancellationToken ct)
    {
        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
            streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        await using var stream = new FileStream(path, streamOptions);
        await using var writer = new StreamWriter(stream);
        foreach (var (key, value) in representable)
            await writer.WriteLineAsync($"{key}={value}".AsMemory(), ct).ConfigureAwait(false);
    }

    // Default to a network-enabled container (clone/restore need it); "none" only when explicitly set.
    private static string NetworkMode(string? network)
        => string.Equals(network, "none", StringComparison.OrdinalIgnoreCase) ? "none" : "bridge";

    private static void TrySetOwnerOnly(string dir)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception)
        {
            // Best-effort: a filesystem that doesn't support Unix modes shouldn't break execution.
        }
    }

    private async Task ForceRemoveContainerAsync(string containerName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("rm");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(containerName);
            using var p = Process.Start(psi)!;
            // Bound the cleanup wait: a wedged daemon must not hang the step's finally block forever.
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await p.WaitForExitAsync(cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Force-remove of container {Name} failed (likely already gone)", containerName);
        }
    }
}
