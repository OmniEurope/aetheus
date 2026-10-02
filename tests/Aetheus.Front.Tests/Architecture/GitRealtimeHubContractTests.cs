// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Guards the repository-scoped SignalR contract. The global Git group was removed because it
/// could broadcast cross-project changes; leaving an obsolete invocation in component disposal
/// makes navigation fail with an unhandled <c>HubException</c> after the page has rendered.
/// </summary>
public class GitRealtimeHubContractTests
{
    [Fact]
    public void GitRepositories_DoesNotInvokeRemovedGlobalGroupMethods()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Aetheus.Front",
            "Components",
            "Git",
            "GitRepositories.razor.cs"));

        Assert.DoesNotContain("\"JoinGlobalGroup\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"LeaveGlobalGroup\"", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
