// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.GitGraph;
using Aetheus.Back.Data.Entities;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class GitGraphServiceTests
{
    private readonly IGitGraphRepository _repo = Substitute.For<IGitGraphRepository>();
    private readonly GitGraphService _sut;

    public GitGraphServiceTests() => _sut = new GitGraphService(_repo);

    [Fact]
    public async Task GetCommitAsync_MapsCrossLinksToDto()
    {
        var commit = new GitCommit
        {
            Id = 5,
            ProjectId = 2,
            Sha = "abc1234",
            Message = "msg",
            Author = "dev",
            Project = new Project { Name = "App", RepositoryUrl = "https://repo" },
            Releases = [new Release { Id = 1, Version = "1.0.0", Status = ReleaseStatus.Published }],
            Artifacts = [new PipelineArtifact { Id = 3, Name = "drop", SizeBytes = 42 }],
            Branches = [new GitBranch { Id = 7, Name = "main" }]
        };
        _repo.FindCommitAsync(5, Arg.Any<CancellationToken>()).Returns(commit);

        var dto = await _sut.GetCommitAsync(5, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.Equal(2, dto!.ProjectId);
        Assert.Equal("App", dto.ProjectName);
        Assert.Equal("https://repo", dto.RepositoryUrl);
        Assert.Equal(1, Assert.Single(dto.Releases).Id);
        Assert.Equal(3, Assert.Single(dto.Artifacts).Id);
        Assert.Equal(7, Assert.Single(dto.Branches).Id);
    }

    [Fact]
    public async Task GetCommitAsync_Missing_ReturnsNull()
    {
        _repo.FindCommitAsync(99, Arg.Any<CancellationToken>()).Returns((GitCommit?)null);

        Assert.Null(await _sut.GetCommitAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetBranchAsync_MapsCrossLinksToDto()
    {
        var branch = new GitBranch
        {
            Id = 4,
            ProjectId = 2,
            Name = "release/v2",
            Project = new Project { Name = "App", RepositoryUrl = "https://repo" },
            Releases = [new Release { Id = 1, Version = "2.0.0", Status = ReleaseStatus.Detected }],
            Artifacts = [new PipelineArtifact { Id = 9, Name = "drop", SizeBytes = 7 }],
            Commits = [new GitCommit { Id = 6, Sha = "def5678", Message = "c" }]
        };
        _repo.FindBranchAsync(4, Arg.Any<CancellationToken>()).Returns(branch);

        var dto = await _sut.GetBranchAsync(4, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.Equal("release/v2", dto!.Name);
        Assert.Equal(1, Assert.Single(dto.Releases).Id);
        Assert.Equal(9, Assert.Single(dto.Artifacts).Id);
        Assert.Equal(6, Assert.Single(dto.Commits).Id);
    }

    [Fact]
    public async Task GetBranchAsync_Missing_ReturnsNull()
    {
        _repo.FindBranchAsync(99, Arg.Any<CancellationToken>()).Returns((GitBranch?)null);

        Assert.Null(await _sut.GetBranchAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetProjectGraphAsync_MapsCommitsAndBranches()
    {
        var commits = new List<GitCommit>
        {
            new()
            {
                Id = 10, ProjectId = 2, Sha = "aaa111", Message = "first",
                Project = new Project { Name = "App", RepositoryUrl = "https://repo" },
                Releases = [new Release { Id = 1, Version = "1.0.0", Status = ReleaseStatus.Published }],
                Artifacts = [new PipelineArtifact { Id = 3, Name = "drop", SizeBytes = 42 }],
                Branches = [new GitBranch { Id = 7, Name = "main" }]
            }
        };
        _repo.FindProjectCommitsAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(commits);
        _repo.FindProjectBranchesAsync(2, Arg.Any<CancellationToken>())
            .Returns([new GitBranch { Id = 7, Name = "main" }]);

        var dto = await _sut.GetProjectGraphAsync(2, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, dto.ProjectId);
        Assert.Equal("App", dto.ProjectName);
        Assert.Equal("https://repo", dto.RepositoryUrl);
        var commit = Assert.Single(dto.Commits);
        Assert.Equal("aaa111", commit.Sha);
        Assert.Equal(1, Assert.Single(commit.Releases).Id);
        Assert.Equal(3, Assert.Single(commit.Artifacts).Id);
        Assert.Equal(7, Assert.Single(dto.Branches).Id);
    }

    [Fact]
    public async Task GetProjectGraphAsync_NoCommits_ReturnsEmptyGraph()
    {
        _repo.FindProjectCommitsAsync(5, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _repo.FindProjectBranchesAsync(5, Arg.Any<CancellationToken>()).Returns([]);

        var dto = await _sut.GetProjectGraphAsync(5, ct: TestContext.Current.CancellationToken);

        Assert.Equal(5, dto.ProjectId);
        Assert.Empty(dto.Commits);
        Assert.Empty(dto.Branches);
        Assert.Null(dto.ProjectName);
    }
}
