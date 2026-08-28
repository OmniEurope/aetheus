// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// Windows displays a firewall consent dialog when a test host opens a listening socket.
/// Agent tests must use in-memory collaborators instead of hosting local network servers.
/// </summary>
public sealed class NetworkListenerAuditTests
{
    [Fact]
    public void AgentCoreTests_DoNotOpenNetworkListeners()
    {
        var forbiddenTokens = new[]
        {
            string.Concat("Tcp", "Listener"),
            string.Concat("Http", "Listener"),
            string.Concat("Accept", "TcpClientAsync"),
            string.Concat("Listen", "AnyIP"),
            string.Concat("Listen", "Localhost"),
            string.Concat(".", "Listen(")
        };
        var testDirectory = Path.Combine(FindRepoRoot(), "tests", "Aetheus.Agent.Core.Tests");
        var violations = RepositoryScan.Enumerate(testDirectory, "*.cs")
            .SelectMany(path => forbiddenTokens
                .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(testDirectory, path)}: {token}"))
            .ToArray();

        Assert.Empty(violations);
    }

    private static string FindRepoRoot() => Aetheus.Agent.Core.Tests.RepositoryScan.Root;
}
