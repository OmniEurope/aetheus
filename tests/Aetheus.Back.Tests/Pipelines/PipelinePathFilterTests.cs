// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// `paths_ignore:` decides not to build. That asymmetry is the whole design: a run that happens
/// needlessly costs time, a run that does NOT happen is invisible, so every uncertainty resolves to
/// building. These tests are mostly about the cases that must NOT skip.
/// </summary>
public sealed class PipelinePathFilterTests
{
    private static readonly string[] DocsOnly = ["docs/**", "**/*.md", ".claude/**"];

    [Fact]
    public void APushTouchingOnlyIgnoredPathsIsSkipped()
    {
        Assert.True(PipelinePathFilter.ShouldSkip(
            DocsOnly, ["docs/plans/PLAN-006.md", "README.md", ".claude/notes.txt"]));
    }

    [Fact]
    public void OneSourceFileAmongTheDocsIsEnoughToBuild()
    {
        // The case that must never regress: a push of three commits where two are documentation.
        Assert.False(PipelinePathFilter.ShouldSkip(
            DocsOnly, ["docs/guide.md", "src/Aetheus.Back/Program.cs", "README.md"]));
    }

    [Fact]
    public void ADefinitionWithNoPatternsNeverSkips()
    {
        Assert.False(PipelinePathFilter.ShouldSkip([], ["README.md"]));
    }

    [Fact]
    public void AnUnknownChangeSetNeverSkips()
    {
        // Null means git could not answer. Treating that as "nothing changed" would silently drop
        // builds whenever the diff failed to read.
        Assert.False(PipelinePathFilter.ShouldSkip(DocsOnly, null));
    }

    [Fact]
    public void AnEmptyChangeSetNeverSkips()
    {
        Assert.False(PipelinePathFilter.ShouldSkip(DocsOnly, []));
    }

    [Theory]
    [InlineData("src/*.cs", "src/Program.cs", true)]
    [InlineData("src/*.cs", "src/nested/Program.cs", false)]
    [InlineData("src/**", "src/nested/deep/Program.cs", true)]
    [InlineData("**/*.md", "a/b/c/readme.md", true)]
    [InlineData("**/*.md", "a/b/c/readme.txt", false)]
    // A leading `**/` means zero or more directories: `**/*.md` is written to cover the whole
    // repository, and reading it strictly would exclude the root-level README it exists for.
    [InlineData("**/*.md", "README.md", true)]
    [InlineData("**/*.md", "README.txt", false)]
    public void GlobsFollowTheSameSemanticsAsTheBranchFilter(string pattern, string path, bool matches)
    {
        // One asterisk stays inside a path segment, two cross it, exactly like `branches:`. An author
        // who learned one filter should not have to learn a second.
        Assert.Equal(matches, PipelinePathFilter.ShouldSkip([pattern], [path]));
    }

    [Fact]
    public void APathIsNormalisedBeforeMatching()
    {
        // Git reports forward slashes; a pattern author should not have to think about the platform
        // the control plane happens to run on.
        Assert.True(PipelinePathFilter.ShouldSkip(["docs/**"], ["docs\\guide.md", "/docs/other.md"]));
    }

    [Fact]
    public void APatternThatMatchesNothingLeavesEveryPushBuilding()
    {
        Assert.False(PipelinePathFilter.ShouldSkip(["never/**"], ["src/Program.cs"]));
    }
}
