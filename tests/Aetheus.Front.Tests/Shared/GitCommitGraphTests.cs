// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Shared;

public class GitCommitGraphTests : BunitContext
{
    public GitCommitGraphTests() => BunitTestHelper.RegisterServices(this);

    private static List<GitLightCommitDto> Chain() =>
    [
        new() { Sha = "ccc333", ShortSha = "ccc333", Message = "merge", AuthorName = "alice",
                AuthorDate = new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc),
                ParentShas = ["bbb222", "aaa111"], RefNames = ["main"] },
        new() { Sha = "bbb222", ShortSha = "bbb222", Message = "feature work", AuthorName = "bob",
                AuthorDate = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
                ParentShas = ["aaa111"] },
        new() { Sha = "aaa111", ShortSha = "aaa111", Message = "init", AuthorName = "alice",
                AuthorDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                ParentShas = [] }
    ];

    [Fact]
    public void Renders_Graph_WithSvgAndCommitMessages()
    {
        var cut = Render<GitCommitGraph>(p => p.Add(c => c.Commits, Chain()));

        Assert.NotEmpty(cut.FindAll("svg"));
        Assert.Equal(3, cut.FindAll("circle").Count);
        var edges = cut.FindAll("path");
        Assert.Equal(3, edges.Count);
        Assert.Contains(edges, edge => edge.GetAttribute("d")!.Contains('C'));
        Assert.Contains("main", cut.Markup);
        Assert.Contains("feature work", cut.Markup);
        Assert.Contains("init", cut.Markup);
    }

    [Fact]
    public void Renders_EmptyState_WhenNoCommits()
    {
        var cut = Render<GitCommitGraph>(p => p.Add(c => c.Commits, []));

        Assert.Empty(cut.FindAll("svg"));
        Assert.Contains("GitGraphEmptyTitle", cut.Markup);
    }
}
