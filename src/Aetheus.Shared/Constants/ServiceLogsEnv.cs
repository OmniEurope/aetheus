// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Constants;

/// <summary>
/// Env-var keys carrying the <see cref="Aetheus.Shared.Enums.OperationKind.ServiceGetLogs"/> options
/// from the backend to the agent. The systemd unit travels in the task target; the line count and follow
/// flag ride in these keys and are re-clamped/parsed agent-side before the argv-only journalctl call.
/// Shared so both sides agree on the names.
/// </summary>
public static class ServiceLogsEnv
{
    public const string Lines = "AETHEUS_SERVICE_LOG_LINES";
    public const string Follow = "AETHEUS_SERVICE_LOG_FOLLOW";
}
