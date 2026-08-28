// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Git;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

public sealed class GitCommitDetailTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public GitCommitDetailTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public async Task RendersRealCommitShape_WithMergeParentsAndFileDiff()
    {
        const string sha = "abcdef1234567890abcdef1234567890abcdef12";
        MockRepository();
        _handler.SetJsonResponse(HttpMethod.Get, $"api/git/repos/7/commits/{sha}", new GitLightCommitDetailDto
        {
            Commit = new GitLightCommitDto
            {
                Sha = sha,
                ShortSha = "abcdef1",
                Message = "merge: feature",
                AuthorName = "Test User",
                AuthorEmail = "test@example.com",
                AuthorDate = DateTime.UtcNow,
                ParentShas =
                [
                    "1111111111111111111111111111111111111111",
                    "2222222222222222222222222222222222222222"
                ]
            },
            Diff = new PullRequestDiffDto
            {
                FileDiffs =
                [
                    new FileDiffDto
                    {
                        Path = "src/Feature.cs",
                        Status = "added",
                        Additions = 1,
                        Patch = """
                            diff --git a/src/Feature.cs b/src/Feature.cs
                            +++ b/src/Feature.cs
                            @@ -0,0 +1 @@
                            +public sealed class Feature;
                            """
                    }
                ],
                Stats = new DiffStatsDto { FilesChanged = 1, Additions = 1 }
            }
        });

        var roundTrip = await Services.GetRequiredService<ApiClient>()
            .Git.GetGitCommitDetailAsync(7, sha, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(roundTrip);

        var cut = Render<GitCommitDetail>(parameters => parameters
            .Add(component => component.RepoId, 7)
            .Add(component => component.Sha, sha));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("merge: feature", cut.Markup);
            Assert.Contains("1111111", cut.Markup);
            Assert.Contains("2222222", cut.Markup);
            Assert.Contains("src/Feature.cs", cut.Markup);
            Assert.Contains("public sealed class Feature", cut.Markup);
            Assert.Contains("GitMergeDiffFirstParent", cut.Markup, StringComparison.Ordinal);
            Assert.Single(cut.FindAll(".git-diff-workspace"));
            Assert.Single(cut.FindAll(".git-focused-diff-body"));
        });
    }

    [Fact]
    public void FileBrowser_SelectsAndFiltersTheFocusedDiff()
    {
        const string sha = "abcdef1234567890abcdef1234567890abcdef12";
        MockRepository();
        _handler.SetJsonResponse(HttpMethod.Get, $"api/git/repos/7/commits/{sha}", new GitLightCommitDetailDto
        {
            Commit = new GitLightCommitDto
            {
                Sha = sha,
                ShortSha = "abcdef1",
                Message = "two files",
                AuthorName = "Test User",
                AuthorDate = DateTime.UtcNow
            },
            Diff = new PullRequestDiffDto
            {
                FileDiffs =
                [
                    new FileDiffDto
                    {
                        Path = "src/First.cs",
                        Status = "added",
                        Additions = 1,
                        Patch = "@@ -0,0 +1 @@\n+public sealed class First;"
                    },
                    new FileDiffDto
                    {
                        Path = "src/Second.cs",
                        Status = "modified",
                        Additions = 1,
                        Deletions = 1,
                        Patch = "@@ -1 +1 @@\n-public sealed class OldSecond;\n+public sealed class Second;"
                    }
                ],
                Stats = new DiffStatsDto { FilesChanged = 2, Additions = 2, Deletions = 1 }
            }
        });

        var cut = Render<GitCommitDetail>(parameters => parameters
            .Add(component => component.RepoId, 7)
            .Add(component => component.Sha, sha));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll(".git-file-option").Count);
            Assert.Contains("public sealed class First", cut.Find(".git-focused-diff-body").TextContent);
            Assert.DoesNotContain("public sealed class Second", cut.Find(".git-focused-diff-body").TextContent);
            Assert.Equal("true", cut.Find("button[aria-label='AllFiles']").GetAttribute("aria-pressed"));
            Assert.Equal("true", cut.Find("button[aria-label='HideDiff']").GetAttribute("aria-expanded"));
        });

        cut.FindAll(".git-file-option")[1].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("public sealed class Second", cut.Find(".git-focused-diff-body").TextContent);
            Assert.DoesNotContain("public sealed class First", cut.Find(".git-focused-diff-body").TextContent);
        });

        cut.Find("button[aria-label='Added']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".git-file-option"));
            Assert.Contains("src/First.cs", cut.Find(".git-file-listbox").TextContent);
            Assert.Contains("public sealed class First", cut.Find(".git-focused-diff-body").TextContent);
            Assert.Equal("true", cut.Find("button[aria-label='Added']").GetAttribute("aria-pressed"));
        });

        cut.Find(".git-file-search").Change("missing");

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".git-file-option"));
            Assert.Contains("NoFilesFound", cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll(".git-focused-diff-body"));
        });
    }

    [Fact]
    public async Task RendersTruncationWarning_WhenBackendCapsPatch()
    {
        const string sha = "abcdef1234567890abcdef1234567890abcdef12";
        MockRepository();
        _handler.SetJsonResponse(HttpMethod.Get, $"api/git/repos/7/commits/{sha}", new GitLightCommitDetailDto
        {
            Commit = new GitLightCommitDto
            {
                Sha = sha,
                ShortSha = "abcdef1",
                Message = "large commit",
                AuthorName = "Test User",
                AuthorDate = DateTime.UtcNow
            },
            Diff = new PullRequestDiffDto { IsTruncated = true }
        });

        var roundTrip = await Services.GetRequiredService<ApiClient>()
            .Git.GetGitCommitDetailAsync(7, sha, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(roundTrip);

        var cut = Render<GitCommitDetail>(parameters => parameters
            .Add(component => component.RepoId, 7)
            .Add(component => component.Sha, sha));

        cut.WaitForAssertion(() => Assert.Contains("GitPatchTruncated", cut.Markup, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("+++ b/file.txt", "diff-line")]
    [InlineData("--- a/file.txt", "diff-line")]
    [InlineData("+++content beginning with pluses", "diff-line diff-line-add")]
    [InlineData("---content beginning with minuses", "diff-line diff-line-del")]
    public void LineClass_DistinguishesHeadersFromContent(string line, string expected)
    {
        Assert.Equal(expected, GitCommitDetail.LineClass(line));
    }

    [Fact]
    public void BuildDiffLines_AssignsOldAndNewLineNumbersAcrossAReplacement()
    {
        const string patch = "@@ -12,2 +12,3 @@ section\n context\n-removed\n+replacement\n+inserted";

        var lines = GitCommitDetail.BuildDiffLines(patch);

        Assert.Collection(lines,
            line => Assert.Equal((null, null), (line.OldLineNumber, line.NewLineNumber)),
            line => Assert.Equal((12, 12), (line.OldLineNumber, line.NewLineNumber)),
            line => Assert.Equal((13, null), (line.OldLineNumber, line.NewLineNumber)),
            line => Assert.Equal((null, 13), (line.OldLineNumber, line.NewLineNumber)),
            line => Assert.Equal((null, 14), (line.OldLineNumber, line.NewLineNumber)));
    }

    private void MockRepository() =>
        _handler.SetJsonResponse(HttpMethod.Get, "api/git/repos/7", new GitLightRepoDto
        {
            Id = 7,
            ProjectId = 3,
            ProjectName = "Aetheus",
            Name = "origin"
        });
}
