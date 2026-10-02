// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Git;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class GitServiceFullTests
{
    private readonly IGitRepository _repoMock = Substitute.For<IGitRepository>();
    private readonly IPipelineRepository _pipelineRepoMock = Substitute.For<IPipelineRepository>();
    private readonly IAuditService _auditMock = Substitute.For<IAuditService>();
    private readonly ILogger<GitService> _loggerMock = Substitute.For<ILogger<GitService>>();
    private readonly GitService _sut;

    public GitServiceFullTests()
    {
        _sut = new GitService(_repoMock, _auditMock, _loggerMock, TimeProvider.System);
    }

    // --- GitConnection CRUD ---

    [Fact]
    public async Task GetConnectionsByProjectAsync_ReturnsMappedList()
    {
        _repoMock.GetConnectionsByProjectAsync(1, Arg.Any<CancellationToken>())
            .Returns([new GitConnection
            {
                Id = 10, ProjectId = 1, ProviderType = GitProviderType.GitHub,
                OwnerOrGroup = "octocat", RepositoryName = "my-repo", AutoSyncEnabled = true
            }]);

        var result = await _sut.GetConnectionsByProjectAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(10, result[0].Id);
        Assert.Equal(GitProviderType.GitHub, result[0].ProviderType);
        Assert.Equal("octocat", result[0].OwnerOrGroup);
    }

    [Fact]
    public async Task GetConnectionDetailAsync_Found_ReturnsDto()
    {
        _repoMock.GetConnectionDetailAsync(10, Arg.Any<CancellationToken>())
            .Returns(new GitConnection { Id = 10, ProjectId = 1, ProviderType = GitProviderType.GitLab, OwnerOrGroup = "group", RepositoryName = "repo" });

        var result = await _sut.GetConnectionDetailAsync(10, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(10, result.Id);
    }

    [Fact]
    public async Task GetConnectionDetailAsync_NotFound_ReturnsNull()
    {
        _repoMock.GetConnectionDetailAsync(999, Arg.Any<CancellationToken>())
            .Returns((GitConnection?)null);

        var result = await _sut.GetConnectionDetailAsync(999, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateConnectionAsync_CreatesAndAudits()
    {
        var request = new CreateGitConnectionRequest
        {
            ProjectId = 1,
            ProviderType = GitProviderType.GitHub,
            OwnerOrGroup = "octocat",
            RepositoryName = "my-repo",
            ServiceConnectionId = 5,
            AutoSyncEnabled = true
        };

        _repoMock.AddConnectionAsync(Arg.Any<GitConnection>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _sut.CreateConnectionAsync(request, ct: TestContext.Current.CancellationToken);

        Assert.Equal(GitProviderType.GitHub, result.ProviderType);
        Assert.Equal("octocat", result.OwnerOrGroup);
        await _repoMock.Received(1).AddConnectionAsync(Arg.Any<GitConnection>(), Arg.Any<CancellationToken>());
        await _auditMock.Received(1).LogAsync("Created", "GitConnection", Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateConnectionAsync_Found_UpdatesAndAudits()
    {
        var entity = new GitConnection { Id = 10, ProjectId = 1, ProviderType = GitProviderType.GitHub, OwnerOrGroup = "a", RepositoryName = "b" };
        _repoMock.FindConnectionAsync(10, Arg.Any<CancellationToken>()).Returns(entity);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.UpdateConnectionAsync(10, new UpdateGitConnectionRequest { ServiceConnectionId = 99, AutoSyncEnabled = false }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(99, entity.ServiceConnectionId);
        Assert.False(entity.AutoSyncEnabled);
        await _auditMock.Received(1).LogAsync("Updated", "GitConnection", 10, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateConnectionAsync_NotFound_ReturnsNull()
    {
        _repoMock.FindConnectionAsync(999, Arg.Any<CancellationToken>()).Returns((GitConnection?)null);

        var result = await _sut.UpdateConnectionAsync(999, new UpdateGitConnectionRequest(), ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteConnectionAsync_Found_DeletesAndAudits()
    {
        var entity = new GitConnection { Id = 10, ProjectId = 1, ProviderType = GitProviderType.GitHub, OwnerOrGroup = "a", RepositoryName = "b" };
        _repoMock.FindConnectionAsync(10, Arg.Any<CancellationToken>()).Returns(entity);

        var result = await _sut.DeleteConnectionAsync(10, ct: TestContext.Current.CancellationToken);

        Assert.True(result);
        await _repoMock.Received(1).RemoveConnectionAsync(entity, Arg.Any<CancellationToken>());
        await _auditMock.Received(1).LogAsync("Deleted", "GitConnection", 10, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteConnectionAsync_NotFound_ReturnsFalse()
    {
        _repoMock.FindConnectionAsync(999, Arg.Any<CancellationToken>()).Returns((GitConnection?)null);

        var result = await _sut.DeleteConnectionAsync(999, ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    // --- Pull Requests ---

    [Fact]
    public async Task GetPullRequestsAsync_ReturnsPaginated()
    {
        var prs = new List<PullRequest>
        {
            new() { Id = 1, GitConnectionId = 10, ExternalId = 100, Title = "PR 1", SourceBranch = "feat", TargetBranch = "main", AuthorLogin = "dev" }
        };
        _repoMock.GetPullRequestsPagedAsync(10, null, 1, 20, null, Arg.Any<CancellationToken>())
            .Returns((prs, 1));

        var result = await _sut.GetPullRequestsAsync(10, new PullRequestPaginationRequest { Page = 1, PageSize = 20 }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("PR 1", result.Items[0].Title);
    }

    [Fact]
    public async Task GetPullRequestsAsync_ClampsPageSize()
    {
        _repoMock.GetPullRequestsPagedAsync(10, null, 1, 200, null, Arg.Any<CancellationToken>())
            .Returns((new List<PullRequest>(), 0));

        await _sut.GetPullRequestsAsync(10, new PullRequestPaginationRequest { Page = 1, PageSize = 999 }, ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).GetPullRequestsPagedAsync(10, null, 1, 200, null, Arg.Any<CancellationToken>());
    }

    // --- SyncPullRequest ---

    [Fact]
    public async Task SyncPullRequestAsync_ExistingPR_Updates()
    {
        var existing = new PullRequest
        {
            Id = 5,
            GitConnectionId = 10,
            ExternalId = 100,
            Title = "Old",
            SourceBranch = "feat",
            TargetBranch = "main",
            AuthorLogin = "dev"
        };
        _repoMock.FindPullRequestByExternalIdAsync(10, 100, Arg.Any<CancellationToken>()).Returns(existing);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var incoming = new PullRequestDto { Title = "Updated", Description = "desc", Status = PullRequestStatus.Merged, HeadCommitSha = "abc123" };
        var result = await _sut.SyncPullRequestAsync(10, 100, incoming, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Title);
        Assert.Equal(PullRequestStatus.Merged, result.Status);
    }

    [Fact]
    public async Task SyncPullRequestAsync_NewPR_Creates()
    {
        _repoMock.FindPullRequestByExternalIdAsync(10, 200, Arg.Any<CancellationToken>()).Returns((PullRequest?)null);
        _repoMock.AddPullRequestAsync(Arg.Any<PullRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var incoming = new PullRequestDto
        {
            Title = "New PR",
            SourceBranch = "feature",
            TargetBranch = "main",
            AuthorLogin = "dev",
            Status = PullRequestStatus.Open,
            ExternalUrl = "https://github.com/pr/200"
        };
        var result = await _sut.SyncPullRequestAsync(10, 200, incoming, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("New PR", result.Title);
        await _repoMock.Received(1).AddPullRequestAsync(Arg.Any<PullRequest>(), Arg.Any<CancellationToken>());
    }

    // --- Branch Policies ---

    [Fact]
    public async Task GetBranchPoliciesAsync_ReturnsMappedList()
    {
        _repoMock.GetBranchPoliciesAsync(10, Arg.Any<CancellationToken>())
            .Returns([new BranchPolicy { Id = 1, GitConnectionId = 10, BranchPattern = "main", PolicyType = BranchPolicyType.RequirePullRequest, IsEnabled = true }]);

        var result = await _sut.GetBranchPoliciesAsync(10, ct: TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("main", result[0].BranchPattern);
    }

    [Fact]
    public async Task CreateBranchPolicyAsync_CreatesAndAudits()
    {
        var request = new CreateBranchPolicyRequest
        {
            GitConnectionId = 10,
            BranchPattern = "main",
            PolicyType = BranchPolicyType.RequirePullRequest,
            IsEnabled = true
        };
        _repoMock.AddBranchPolicyAsync(Arg.Any<BranchPolicy>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.CreateBranchPolicyAsync(request, ct: TestContext.Current.CancellationToken);

        Assert.Equal("main", result.BranchPattern);
        await _auditMock.Received(1).LogAsync("Created", "BranchPolicy", Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateBranchPolicyAsync_Found_UpdatesAndAudits()
    {
        var entity = new BranchPolicy { Id = 1, GitConnectionId = 10, BranchPattern = "main", PolicyType = BranchPolicyType.RequirePullRequest };
        _repoMock.FindBranchPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(entity);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var request = new UpdateBranchPolicyRequest
        {
            BranchPattern = "develop",
            PolicyType = BranchPolicyType.BuildValidation,
            IsEnabled = false
        };
        var result = await _sut.UpdateBranchPolicyAsync(1, request, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("develop", result.BranchPattern);
        await _auditMock.Received(1).LogAsync("Updated", "BranchPolicy", 1, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateBranchPolicyAsync_NotFound_ReturnsNull()
    {
        _repoMock.FindBranchPolicyAsync(999, Arg.Any<CancellationToken>()).Returns((BranchPolicy?)null);

        var result = await _sut.UpdateBranchPolicyAsync(999, new UpdateBranchPolicyRequest(), ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteBranchPolicyAsync_Found_DeletesAndAudits()
    {
        var entity = new BranchPolicy { Id = 1, GitConnectionId = 10, BranchPattern = "main", PolicyType = BranchPolicyType.RequirePullRequest };
        _repoMock.FindBranchPolicyAsync(1, Arg.Any<CancellationToken>()).Returns(entity);

        var result = await _sut.DeleteBranchPolicyAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(result);
        await _repoMock.Received(1).RemoveBranchPolicyAsync(entity, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteBranchPolicyAsync_NotFound_ReturnsFalse()
    {
        _repoMock.FindBranchPolicyAsync(999, Arg.Any<CancellationToken>()).Returns((BranchPolicy?)null);

        var result = await _sut.DeleteBranchPolicyAsync(999, ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task ReportPipelineStatusAsync_DoesNotThrow()
    {
        // P-43: ReportPipelineStatusAsync is currently a local-only placeholder (no provider call yet),
        // so the contract this test guards is that it completes without throwing. Named to match the
        // assertion rather than promising a log verification the test does not perform.
        var report = new PipelineStatusReport { PipelineRunId = 42, State = "success", Context = "ci/build" };

        var ex = await Record.ExceptionAsync(() => _sut.ReportPipelineStatusAsync(report, ct: TestContext.Current.CancellationToken));

        Assert.Null(ex);
    }
}
