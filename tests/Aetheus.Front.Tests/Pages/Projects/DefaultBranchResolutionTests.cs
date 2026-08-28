// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Projects.ProjectDetailSections;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// DefaultBranch resolution must be case-insensitive: a stored "Main"/"MASTER" still matches the
/// repo's real branch, and the main>master>first preference ignores casing (user requirement).
/// </summary>
public class DefaultBranchResolutionTests
{
    [Fact]
    public void KeepsExistingValue_NormalisingToActualCasing()
    {
        var branches = new[] { "develop", "Main", "feature/x" };
        // Stored "main" (lowercase) must resolve to the repo's actual "Main".
        Assert.Equal("Main", ProjectEditSection.ResolveDefaultBranch(branches, "main"));
    }

    [Fact]
    public void PrefersMain_CaseInsensitive_WhenCurrentMissing()
    {
        var branches = new[] { "develop", "MAIN", "master" };
        Assert.Equal("MAIN", ProjectEditSection.ResolveDefaultBranch(branches, current: null));
    }

    [Fact]
    public void FallsBackToMaster_CaseInsensitive_WhenNoMain()
    {
        var branches = new[] { "develop", "Master", "release" };
        Assert.Equal("Master", ProjectEditSection.ResolveDefaultBranch(branches, current: ""));
    }

    [Fact]
    public void PreservesStoredBranch_WhenItIsAbsentFromRepository()
    {
        var branches = new[] { "develop", "release" };
        Assert.Equal("stale-gone", ProjectEditSection.ResolveDefaultBranch(branches, current: "stale-gone"));
    }

    [Fact]
    public void NoBranches_KeepsCurrentOrDefaultsToMain()
    {
        Assert.Equal("main", ProjectEditSection.ResolveDefaultBranch([], current: null));
        Assert.Equal("custom", ProjectEditSection.ResolveDefaultBranch([], current: "custom"));
    }
}
