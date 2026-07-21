// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Constants;

/// <summary>
/// Env-var keys carrying the <see cref="Aetheus.Shared.Enums.OperationKind.TeamspeakSetup"/>
/// parameters from the backend to the agent. The install path travels in the task target; the voice
/// and query ports ride in these keys. No secret is involved - the root-owned teamspeak-setup helper
/// generates the serveradmin credential itself and drops it into the agent-readable credentials file.
/// Shared so both sides agree on the names.
/// </summary>
public static class TeamspeakSetupEnv
{
    public const string VoicePort = "AETHEUS_TEAMSPEAK_VOICE_PORT";
    public const string QueryPort = "AETHEUS_TEAMSPEAK_QUERY_PORT";

    // Runtime ServerQuery ops (graceful restart) - the live query port + the warning broadcast params.
    // The runtime query port key mirrors what TeamspeakServerQueryOperationExecutor already reads.
    public const string RuntimeQueryPort = "TEAMSPEAK_QUERY_PORT";
    public const string WarnSeconds = "TEAMSPEAK_WARN_SECONDS";
    public const string WarnMessage = "TEAMSPEAK_WARN_MESSAGE";
}
