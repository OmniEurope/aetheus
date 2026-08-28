// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests;

public sealed class FastReleaseSafetyContractTests
{
    [Fact]
    public void ManagedFastRelease_RollsBackTheHostWhenReleaseRecordingFails()
    {
        var deploy = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "deploy", "scripts", "release-fast-deploy.sh"));
        var finalize = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "deploy", "scripts", "finalize-fast-release-transaction.sh"));

        Assert.Contains("HOST_COMMITTED_PENDING_RELEASE", deploy, StringComparison.Ordinal);
        Assert.Contains("previous-live", deploy, StringComparison.Ordinal);
        Assert.Contains("previous-source-commit", deploy, StringComparison.Ordinal);
        Assert.Contains("HOST_COMMITTED_PENDING_RELEASE", finalize, StringComparison.Ordinal);
        Assert.Contains("start \"back-$PREVIOUS_LIVE\" \"front-$PREVIOUS_LIVE\"", finalize, StringComparison.Ordinal);
        Assert.Contains("upstream.before", finalize, StringComparison.Ordinal);
        Assert.Contains("source-commit.rollback", finalize, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedFastRelease_PackagesOnlyTheApplicationPayloadRequiredByCandidate()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "deploy", "scripts", "package-fast-release-artifacts.sh"));

        Assert.Contains("aetheus-back.tar.gz", script, StringComparison.Ordinal);
        Assert.Contains("aetheus-front.tar.gz", script, StringComparison.Ordinal);
        Assert.Contains("qa-rollback-contract", script, StringComparison.Ordinal);
        Assert.Contains("printf '%s\\n' 4", script, StringComparison.Ordinal);
        Assert.Contains("delivery-contract.json", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Aetheus.slnx", script, StringComparison.Ordinal);
        Assert.DoesNotContain("aetheus-browser-smoke", script, StringComparison.Ordinal);
        Assert.DoesNotContain("agent-release", script, StringComparison.Ordinal);
        Assert.DoesNotContain("aetheus-vitrine", script, StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
