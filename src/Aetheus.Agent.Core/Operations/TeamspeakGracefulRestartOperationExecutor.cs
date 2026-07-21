// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Aetheus.Agent.Core.Collectors;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Shared.Constants;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Composite TeamSpeak maintenance ops that the old shell path could not run (the agent's CommandValidator
/// rejected the <c>printf|nc &amp;&amp; sleep &amp;&amp; ... &amp;&amp; systemctl restart</c> chain and the
/// <c>tail | sort | tail</c> log pipe):
/// <list type="bullet">
/// <item><b>Graceful restart</b> - broadcast a warning (gm) over the native ServerQuery TCP client, wait
/// the grace period, kick everyone, then restart the <c>ts3server</c> systemd unit via the service-control
/// sudoers grant. Each phase reports honestly; a failed restart is a non-zero exit (never a fake green).</item>
/// <item><b>Log read</b> - tail the newest file under <c>{installPath}/logs/</c> by direct file read
/// (read access via the teamspeak group), no shell pipe.</item>
/// </list>
/// </summary>
public sealed class TeamspeakGracefulRestartOperationExecutor(
    ITeamspeakQueryClient queryClient,
    IOptions<AetheusAgentOptions> options,
    ILogger<TeamspeakGracefulRestartOperationExecutor> logger) : IOperationExecutor
{
    private readonly AetheusAgentOptions _options = options.Value;

    public bool CanHandle(OperationKind kind) =>
        kind is OperationKind.TeamspeakGracefulRestart or OperationKind.TeamspeakGetLogs;

    public Task<ExecutorResult> ExecuteAsync(
        OperationKind kind, string target, int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken cancellationToken)
        => ExecuteAsync(kind, target, new Dictionary<string, string>(), timeoutSeconds, onOutput, cancellationToken);

    public Task<ExecutorResult> ExecuteAsync(
        OperationKind kind, string target,
        IReadOnlyDictionary<string, string> envVars,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
        => kind == OperationKind.TeamspeakGetLogs
            ? GetLogsAsync(target, onOutput)
            : GracefulRestartAsync(envVars, timeoutSeconds, onOutput, cancellationToken);

    private async Task<ExecutorResult> GracefulRestartAsync(
        IReadOnlyDictionary<string, string> envVars,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken ct)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await onOutput("TeamSpeak operations are only supported on Linux", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        if (!envVars.TryGetValue(TeamspeakSetupEnv.RuntimeQueryPort, out var portRaw) ||
            !int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            await onOutput("Missing or invalid TEAMSPEAK_QUERY_PORT", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        var seconds = 0;
        if (envVars.TryGetValue(TeamspeakSetupEnv.WarnSeconds, out var secRaw))
            int.TryParse(secRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds);
        seconds = Math.Clamp(seconds, 0, 600);
        var message = envVars.GetValueOrDefault(TeamspeakSetupEnv.WarnMessage, "Server restarting");

        var credentialPath = ServerQueryHelper.ReadCredentialPath();
        string credential;
        try
        {
            credential = (await File.ReadAllTextAsync(credentialPath, ct).ConfigureAwait(false)).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await onOutput($"Cannot read TeamSpeak query credential at {credentialPath}: {ex.Message}", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }
        if (string.IsNullOrEmpty(credential))
        {
            await onOutput("TeamSpeak query credential file is empty", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        // 1) Broadcast the warning, 2) wait the grace period, 3) kick everyone. Each ServerQuery runs
        //    over the native TCP client (no shell, no nc). A failed gm/kick is logged but does not abort
        //    the restart - the operator asked for a restart.
        var escCred = ServerQueryHelper.EscapeServerQuery(credential);
        var gm = $"login serveradmin {escCred}\nuse sid=1\ngm msg={ServerQueryHelper.EscapeServerQuery(message)}\nquit\n";
        await onOutput($"Broadcasting restart warning (grace {seconds}s)…", TaskLogLevel.Info).ConfigureAwait(false);
        await queryClient.ExecuteAsync(port, gm, ct).ConfigureAwait(false);

        if (seconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);

        var kick = $"login serveradmin {escCred}\nuse sid=1\nclientkick reasonid=5 reasonmsg=Restart cldbid=0\nquit\n";
        await onOutput("Kicking connected clients…", TaskLogLevel.Info).ConfigureAwait(false);
        await queryClient.ExecuteAsync(port, kick, ct).ConfigureAwait(false);

        // 4) Restart the systemd unit. `ts3server` is allow-listed in the AETHEUS_SYSTEMCTL sudoers set
        //    (service-control capability); without it sudo fails and the step fails honestly.
        var restartTimeout = Math.Clamp(timeoutSeconds, _options.MinTimeoutSeconds, _options.MaxTimeoutSeconds);
        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add("/bin/systemctl");
        psi.ArgumentList.Add("restart");
        psi.ArgumentList.Add("ts3server");

        await onOutput("Restarting ts3server…", TaskLogLevel.Info).ConfigureAwait(false);
        return await ProcessRunner.RunAsync(psi, restartTimeout, onOutput, logger, ct).ConfigureAwait(false);
    }

    private async Task<ExecutorResult> GetLogsAsync(
        string installPath,
        Func<string, TaskLogLevel, Task> onOutput)
    {
        var logsDir = Path.Combine(installPath, "logs");
        if (!Directory.Exists(logsDir))
        {
            await onOutput($"No TeamSpeak logs directory at {logsDir}", TaskLogLevel.Warning).ConfigureAwait(false);
            return new ExecutorResult(0, false); // nothing to show - honest empty success, not a failure
        }

        string? newest = null;
        var newestTime = DateTime.MinValue;
        try
        {
            foreach (var file in Directory.EnumerateFiles(logsDir, "*.log"))
            {
                var t = File.GetLastWriteTimeUtc(file);
                if (t >= newestTime) { newestTime = t; newest = file; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await onOutput($"Could not enumerate {logsDir}: {ex.Message}", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        if (newest is null)
        {
            await onOutput($"No .log files under {logsDir}", TaskLogLevel.Warning).ConfigureAwait(false);
            return new ExecutorResult(0, false);
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(newest).ConfigureAwait(false);
            var tail = lines.Length <= 100 ? lines : lines[^100..];
            await onOutput(string.Join('\n', tail), TaskLogLevel.Info).ConfigureAwait(false);
            return new ExecutorResult(0, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await onOutput($"Could not read {newest}: {ex.Message}", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }
    }
}
