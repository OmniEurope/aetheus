// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.GitGraph;
using Aetheus.Back.Data.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class GitGraphRecorderTests
{
    private readonly IGitGraphRepository _repo = Substitute.For<IGitGraphRepository>();
    private readonly GitGraphRecorder _sut;

    public GitGraphRecorderTests()
        => _sut = new GitGraphRecorder(_repo, TimeProvider.System, NullLogger<GitGraphRecorder>.Instance);

    [Fact]
    public async Task RecordRunContextAsync_CreatesAndLinks_WhenMissing()
    {
        _repo.FindCommitByShaAsync(2, "sha", Arg.Any<CancellationToken>()).Returns((GitCommit?)null);
        _repo.FindBranchByNameAsync(2, "main", Arg.Any<CancellationToken>()).Returns((GitBranch?)null);

        GitCommit? tracked = null;
        _repo.When(r => r.TrackCommit(Arg.Any<GitCommit>())).Do(ci => tracked = ci.Arg<GitCommit>());

        await _sut.RecordRunContextAsync(2, "sha", "main", ct: TestContext.Current.CancellationToken);

        _repo.Received(1).TrackCommit(Arg.Is<GitCommit>(c => c.Sha == "sha" && c.ProjectId == 2));
        _repo.Received(1).TrackBranch(Arg.Is<GitBranch>(b => b.Name == "main" && b.ProjectId == 2));
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.NotNull(tracked);
        Assert.Single(tracked!.Branches); // commit linked to the branch
    }

    [Fact]
    public async Task RecordRunContextAsync_LinksExistingCommitToBranch()
    {
        var commit = new GitCommit { Id = 5, ProjectId = 2, Sha = "sha" };
        var branch = new GitBranch { Id = 7, ProjectId = 2, Name = "main" };
        _repo.FindCommitByShaAsync(2, "sha", Arg.Any<CancellationToken>()).Returns(commit);
        _repo.FindBranchByNameAsync(2, "main", Arg.Any<CancellationToken>()).Returns(branch);

        await _sut.RecordRunContextAsync(2, "sha", "main", ct: TestContext.Current.CancellationToken);

        _repo.DidNotReceive().TrackCommit(Arg.Any<GitCommit>());
        Assert.Contains(branch, commit.Branches);
    }

    [Fact]
    public async Task RecordReleaseContextAsync_LinksCommitAndBranchToRelease()
    {
        var release = new Release { Id = 3, ProjectId = 2, CommitHash = "sha", BranchName = "main" };
        var commit = new GitCommit { Id = 5, ProjectId = 2, Sha = "sha" };
        var branch = new GitBranch { Id = 7, ProjectId = 2, Name = "main" };
        _repo.FindCommitByShaAsync(2, "sha", Arg.Any<CancellationToken>()).Returns(commit);
        _repo.FindBranchByNameAsync(2, "main", Arg.Any<CancellationToken>()).Returns(branch);

        await _sut.RecordReleaseContextAsync(release, ct: TestContext.Current.CancellationToken);

        Assert.Contains(release, commit.Releases);
        Assert.Contains(release, branch.Releases);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordRunContextAsync_NoOp_WhenNoGitContext()
    {
        await _sut.RecordRunContextAsync(2, null, null, ct: TestContext.Current.CancellationToken);

        await _repo.DidNotReceive().FindCommitByShaAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordRunContextAsync_BestEffort_SwallowsSaveFailure()
    {
        _repo.FindCommitByShaAsync(2, "sha", Arg.Any<CancellationToken>()).Returns((GitCommit?)null);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("unique race"));

        // Must not throw - a recording failure can never break the run-trigger primary flow.
        await _sut.RecordRunContextAsync(2, "sha", "main", ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolveRunLinksAsync_ReturnsLinks_WhenNodesExist()
    {
        _repo.FindCommitByShaReadOnlyAsync(2, "sha", Arg.Any<CancellationToken>())
            .Returns(new GitCommit { Id = 5, Sha = "sha", Message = "m" });
        _repo.FindBranchByNameReadOnlyAsync(2, "main", Arg.Any<CancellationToken>())
            .Returns(new GitBranch { Id = 7, Name = "main" });

        var (commits, branches) = await _sut.ResolveRunLinksAsync(2, "sha", "main", ct: TestContext.Current.CancellationToken);

        Assert.Equal(5, Assert.Single(commits).Id);
        Assert.Equal(7, Assert.Single(branches).Id);
    }

    [Fact]
    public async Task ResolveRunLinksAsync_Empty_WhenNoNodes()
    {
        _repo.FindCommitByShaReadOnlyAsync(2, "sha", Arg.Any<CancellationToken>()).Returns((GitCommit?)null);
        _repo.FindBranchByNameReadOnlyAsync(2, "main", Arg.Any<CancellationToken>()).Returns((GitBranch?)null);

        var (commits, branches) = await _sut.ResolveRunLinksAsync(2, "sha", "main", ct: TestContext.Current.CancellationToken);

        Assert.Empty(commits);
        Assert.Empty(branches);
    }
}
