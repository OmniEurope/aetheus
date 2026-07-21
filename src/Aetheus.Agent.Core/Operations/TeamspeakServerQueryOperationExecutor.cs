// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Runtime.InteropServices;
using Aetheus.Agent.Core.Collectors;
using Aetheus.Agent.Core.Executors;
using Aetheus.Shared.Enums;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// S-TECH-87: runs a single TeamSpeak ServerQuery write/query command over a native
/// <see cref="ITeamspeakQueryClient"/> (TcpClient) instead of the legacy <c>printf … | nc</c> shell
/// task. The inner ServerQuery line arrives as the operation target (already
/// <c>EscapeServerQuery</c>-encoded server-side); the query port is in <c>TEAMSPEAK_QUERY_PORT</c>.
/// The serveradmin credential is read from the agent's local credentials file and never travels in
/// the task, so this runs as the unprivileged agent process - no shell, no <c>nc</c>, no sudo hop.
/// </summary>
public sealed class TeamspeakServerQueryOperationExecutor(ITeamspeakQueryClient queryClient) : IOperationExecutor
{
    public bool CanHandle(OperationKind kind) => kind == OperationKind.TeamspeakServerQuery;

    public Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
        => ExecuteAsync(kind, target, new Dictionary<string, string>(), timeoutSeconds, onOutput, cancellationToken);

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        IReadOnlyDictionary<string, string> envVars,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await onOutput("TeamSpeak ServerQuery operations are only supported on Linux", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        // S-TECH-W9K7: a secret-bearing command (channel/server password, token value, snapshot blob)
        // travels in the encrypted TEAMSPEAK_QUERY_CMD env var instead of the plaintext-at-rest Command
        // column; prefer it when present and fall back to the operation target otherwise.
        var queryCommand = envVars.TryGetValue("TEAMSPEAK_QUERY_CMD", out var protectedCmd) && !string.IsNullOrWhiteSpace(protectedCmd)
            ? protectedCmd
            : target;

        if (string.IsNullOrWhiteSpace(queryCommand))
        {
            await onOutput("Empty ServerQuery command", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        if (!envVars.TryGetValue("TEAMSPEAK_QUERY_PORT", out var portRaw) ||
            !int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            await onOutput("Missing or invalid TEAMSPEAK_QUERY_PORT", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        var credentialPath = ServerQueryHelper.ReadCredentialPath();
        string credential;
        try
        {
            credential = (await File.ReadAllTextAsync(credentialPath, cancellationToken).ConfigureAwait(false)).Trim();
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

        // The TCP client writes the script verbatim and reads until the server closes on `quit`.
        // The credential is EscapeServerQuery-encoded so a password with spaces/special chars cannot
        // break the login line; the command was already encoded server-side.
        var script = $"login serveradmin {ServerQueryHelper.EscapeServerQuery(credential)}\nuse sid=1\n{queryCommand}\nquit\n";
        var reply = await queryClient.ExecuteAsync(port, script, cancellationToken).ConfigureAwait(false);

        if (reply is null)
        {
            await onOutput($"No reply from TeamSpeak ServerQuery on port {port} (connection failed or timed out)", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        await onOutput(reply, TaskLogLevel.Info).ConfigureAwait(false);

        // TS3 ServerQuery reports the outcome with a trailing `error id=N msg=…` line. id=0 is success;
        // a failed login or a rejected command returns a non-zero id. Map that to a non-zero exit so a
        // failed action is marked failed, never a fake green (anti-fake rule).
        return ServerQueryReportsSuccess(reply)
            ? new ExecutorResult(0, false)
            : new ExecutorResult(1, false);
    }

    internal static bool ServerQueryReportsSuccess(string reply)
    {
        var lastError = reply
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.StartsWith("error id=", StringComparison.Ordinal));
        return lastError is not null && lastError.StartsWith("error id=0", StringComparison.Ordinal);
    }
}
