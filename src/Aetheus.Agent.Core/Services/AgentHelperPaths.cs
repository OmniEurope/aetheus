// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Services;

/// <summary>
/// Where the installer puts the root-owned helpers the agent calls through sudo (certbot, Apache
/// reload, firewall...). Published to every task as <see cref="VariableName"/> so a deployment script
/// names the directory the agent actually uses instead of restating it (PLAN-003 2.1).
/// </summary>
internal static class AgentHelperPaths
{
    internal const string VariableName = "AGENT_HELPERS_DIR";

    /// <summary>The Linux installer's directory (<c>install-agent-linux.sh</c>, AETHEUS_HELPER_DIR).
    /// There is no equivalent on Windows, where nothing is published.</summary>
    internal const string LinuxDirectory = "/usr/local/lib/aetheus";
}
