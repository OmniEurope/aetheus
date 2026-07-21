// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;

namespace Aetheus.Back.Tests;

public class PipelineBranchFilterTests
{
    [Fact]
    public void Matches_EmptyFilter_MatchesAnyBranch()
    {
        Assert.True(PipelineBranchFilter.Matches([], "refs/heads/main"));
        Assert.True(PipelineBranchFilter.Matches([], "refs/heads/feature/x"));
        Assert.True(PipelineBranchFilter.Matches(null, "refs/heads/anything"));
    }

    [Fact]
    public void Matches_EmptyFilter_MatchesTagsToo()
    {
        // No filter preserves the pre-filter behaviour: fire on anything, including a tag push.
        Assert.True(PipelineBranchFilter.Matches([], "refs/tags/v1.0.0"));
    }

    [Fact]
    public void Matches_WhitespaceOnlyEntries_TreatedAsNoFilter()
    {
        Assert.True(PipelineBranchFilter.Matches(["", "   "], "refs/heads/whatever"));
    }

    [Fact]
    public void Matches_ExactBranch_StripsRefsHeadsPrefix()
    {
        Assert.True(PipelineBranchFilter.Matches(["main"], "refs/heads/main"));
        Assert.True(PipelineBranchFilter.Matches(["develop"], "refs/heads/develop"));
    }

    [Fact]
    public void Matches_ExactBranch_WithoutPrefix()
    {
        // Some payloads may already carry a bare branch name.
        Assert.True(PipelineBranchFilter.Matches(["main"], "main"));
    }

    [Fact]
    public void Matches_ExactBranch_NoMatch()
    {
        Assert.False(PipelineBranchFilter.Matches(["main"], "refs/heads/develop"));
    }

    [Fact]
    public void Matches_IsCaseSensitive()
    {
        Assert.False(PipelineBranchFilter.Matches(["Main"], "refs/heads/main"));
    }

    [Fact]
    public void Matches_MultiplePatterns_AnyMatchWins()
    {
        var filter = new[] { "main", "develop" };
        Assert.True(PipelineBranchFilter.Matches(filter, "refs/heads/develop"));
        Assert.True(PipelineBranchFilter.Matches(filter, "refs/heads/main"));
        Assert.False(PipelineBranchFilter.Matches(filter, "refs/heads/hotfix"));
    }

    [Theory]
    // GitHub Actions semantics: '*' is bounded to one path segment, '**' crosses '/'.
    [InlineData("release/*", "refs/heads/release/1.0", true)]
    [InlineData("release/*", "refs/heads/release/1.0/rc1", false)] // '*' does NOT cross a slash
    [InlineData("release/**", "refs/heads/release/1.0/rc1", true)] // '**' crosses slashes
    [InlineData("release/*", "refs/heads/release", false)]         // needs the slash segment
    [InlineData("release/*", "refs/heads/main", false)]
    [InlineData("feature/*", "refs/heads/feature/login", true)]
    [InlineData("*", "refs/heads/anything", true)]                 // single segment
    [InlineData("*", "refs/heads/feature/login", false)]           // '*' won't span the slash
    [InlineData("**", "refs/heads/feature/deep/branch", true)]     // '**' spans everything
    [InlineData("feat*", "refs/heads/feature", true)]              // prefix glob, one segment
    public void Matches_GlobPatterns(string pattern, string gitRef, bool expected)
    {
        Assert.Equal(expected, PipelineBranchFilter.Matches([pattern], gitRef));
    }

    [Theory]
    // A fully-qualified pattern (copy-pasted from provider docs) gates the same branch as the bare name.
    [InlineData("refs/heads/main", "refs/heads/main", true)]
    [InlineData("refs/heads/main", "refs/heads/develop", false)]
    [InlineData("refs/heads/release/*", "refs/heads/release/1.0", true)]
    public void Matches_FullyQualifiedPattern(string pattern, string gitRef, bool expected)
    {
        Assert.Equal(expected, PipelineBranchFilter.Matches([pattern], gitRef));
    }

    [Fact]
    public void Matches_NonEmptyFilter_RejectsTagPush()
    {
        // An author who set a branch filter does not want tag pushes to trigger the pipeline.
        Assert.False(PipelineBranchFilter.Matches(["main"], "refs/tags/v1.0.0"));
        Assert.False(PipelineBranchFilter.Matches(["*"], "refs/tags/v1.0.0"));
    }

    [Fact]
    public void Matches_NonEmptyFilter_RejectsEmptyRef()
    {
        Assert.False(PipelineBranchFilter.Matches(["main"], ""));
        Assert.False(PipelineBranchFilter.Matches(["main"], null));
    }

    [Fact]
    public void Matches_GlobSpecialCharsAreLiteral()
    {
        // A dot in the pattern is a literal dot, not a regex "any char".
        Assert.True(PipelineBranchFilter.Matches(["release/1.0"], "refs/heads/release/1.0"));
        Assert.False(PipelineBranchFilter.Matches(["release/1.0"], "refs/heads/release/1x0"));
    }

    [Fact]
    public void DeclaresFilter_YamlWithBranchesKey_ReturnsTrue()
    {
        Assert.True(PipelineBranchFilter.DeclaresFilter("trigger: webhook\nbranches:\n  - main\n"));
    }

    [Fact]
    public void DeclaresFilter_IndentedBranchesKey_ReturnsTrue()
    {
        // The declared-filter check is a fail-closed signal used when structured parsing already
        // failed, so it must still catch a `branches:` key nested under indentation.
        Assert.True(PipelineBranchFilter.DeclaresFilter("trigger: webhook\n  branches:\n    - main\n"));
    }

    [Fact]
    public void DeclaresFilter_NoBranchesKey_ReturnsFalse()
    {
        Assert.False(PipelineBranchFilter.DeclaresFilter("trigger: webhook\nstages: [\n"));
    }

    [Fact]
    public void DeclaresFilter_NullOrWhitespace_ReturnsFalse()
    {
        Assert.False(PipelineBranchFilter.DeclaresFilter(null));
        Assert.False(PipelineBranchFilter.DeclaresFilter(""));
        Assert.False(PipelineBranchFilter.DeclaresFilter("   "));
    }
}
