// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// R-249: the host configuration files <c>deploy/scripts/install-agent-linux.sh</c> writes (sudoers
/// drop-ins, root-owned helpers, systemd units) are versioned templates under
/// <c>deploy/agent-host-config</c>, rendered by the installer's <c>render_host_config</c>. A template
/// holds the former heredoc text, with <c>#{NAME}#</c> where the heredoc expanded <c>$NAME</c>.
/// </summary>
internal static class LinuxHostConfigTemplates
{
    public static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepositoryScan.Root, "deploy", "agent-host-config", relative))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
}
