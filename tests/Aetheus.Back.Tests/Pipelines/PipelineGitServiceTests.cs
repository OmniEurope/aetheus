// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Git;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class PipelineGitServiceTests
{
    private readonly IGitLightService _gitServiceMock = Substitute.For<IGitLightService>();
    private readonly IGitLightCliService _cliMock = Substitute.For<IGitLightCliService>();
    private readonly IPipelineRepository _pipelineRepoMock = Substitute.For<IPipelineRepository>();
    private readonly PipelineGitService _sut;

    public PipelineGitServiceTests()
    {
        _sut = new PipelineGitService(
            _gitServiceMock,
            _cliMock,
            _pipelineRepoMock,
            NullLogger<PipelineGitService>.Instance);
    }

    // --- ReadProjectPipelineYamlAsync ---

    [Fact]
    public async Task ReadProjectPipelineYamlAsync_NoRepo_ReturnsNull()
    {
        _gitServiceMock.GetRepositoriesAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<GitLightRepoDto>());

        var result = await _sut.ReadProjectPipelineYamlAsync(1, "deploy", ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadProjectPipelineYamlAsync_EmptyRepo_ReturnsNull()
    {
        _gitServiceMock.GetRepositoriesAsync(1, Arg.Any<CancellationToken>())
            .Returns([new GitLightRepoDto { Id = 1, ProjectId = 1, Slug = "my-repo", IsEmpty = true }]);

        var result = await _sut.ReadProjectPipelineYamlAsync(1, "deploy", ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadProjectPipelineYamlAsync_DirectBlobMatch_ReturnsYaml()
    {
        var yaml = "name: deploy\ntrigger: manual\nstages:\n  - name: build\n    steps:\n      - name: compile\n        shell: dotnet build";
        SetupRepoWithDiskPath(1, "my-repo", "main");

        _cliMock.GetBlobAsync(Arg.Any<string>(), "main", ".pipeline/deploy.yaml", Arg.Any<CancellationToken>())
            .Returns(new GitLightBlobDto { Content = yaml, Path = ".pipeline/deploy.yaml" });

        var result = await _sut.ReadProjectPipelineYamlAsync(1, "deploy", ct: TestContext.Current.CancellationToken);

        Assert.Equal(yaml, result);
    }

    [Fact]
    public async Task ReadProjectPipelineYamlAsync_NoDirectBlob_FallsBackToTreeSearch()
    {
        var yaml = "name: my-pipeline\ntrigger: manual\nstages:\n  - name: build\n    steps:\n      - name: compile\n        shell: dotnet build";
        SetupRepoWithDiskPath(1, "my-repo", "main");

        // Direct lookup returns nothing
        _cliMock.GetBlobAsync(Arg.Any<string>(), "main", ".pipeline/my-pipeline.yaml", Arg.Any<CancellationToken>())
            .Returns(new GitLightBlobDto { Content = null, Path = ".pipeline/my-pipeline.yaml" });

        // Tree has a different filename
        _cliMock.GetTreeAsync(Arg.Any<string>(), "main", ".pipeline", Arg.Any<CancellationToken>())
            .Returns([new GitLightTreeEntryDto { Name = "other.yaml", Type = GitTreeEntryType.Blob }]);

        _cliMock.GetBlobAsync(Arg.Any<string>(), "main", ".pipeline/other.yaml", Arg.Any<CancellationToken>())
            .Returns(new GitLightBlobDto { Content = yaml, Path = ".pipeline/other.yaml" });

        var result = await _sut.ReadProjectPipelineYamlAsync(1, "my-pipeline", ct: TestContext.Current.CancellationToken);

        Assert.Equal(yaml, result);
    }

    [Fact]
    public async Task ReadProjectPipelineYamlAsync_UsesDefaultBranch()
    {
        SetupRepoWithDiskPath(1, "my-repo", "develop");

        _cliMock.GetBlobAsync(Arg.Any<string>(), "develop", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GitLightBlobDto { Content = null });

        _cliMock.GetTreeAsync(Arg.Any<string>(), "develop", ".pipeline", Arg.Any<CancellationToken>())
            .Returns(new List<GitLightTreeEntryDto>());

        var result = await _sut.ReadProjectPipelineYamlAsync(1, "deploy", ct: TestContext.Current.CancellationToken);

        // Verify it used the repo's default branch
        await _cliMock.Received().GetBlobAsync(Arg.Any<string>(), "develop", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadProjectPipelineYamlAtRevisionAsync_UsesExactCommit()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        SetupRepoWithDiskPath(1, "my-repo", "develop");
        var yaml = "name: deploy\ntrigger: manual\nstages: []";
        _cliMock.GetBlobAsync(Arg.Any<string>(), commit, ".pipeline/deploy.yaml", Arg.Any<CancellationToken>())
            .Returns(new GitLightBlobDto { Content = yaml, Path = ".pipeline/deploy.yaml" });

        var result = await _sut.ReadProjectPipelineYamlAtRevisionAsync(1, "deploy", commit, ct: TestContext.Current.CancellationToken);

        Assert.Equal(yaml, result);
        await _cliMock.Received().GetBlobAsync(
            Arg.Any<string>(), commit, ".pipeline/deploy.yaml", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadProjectPipelineYamlAsync_MultipleRepositoriesWithoutSelection_IsRejected()
    {
        _gitServiceMock.GetRepositoriesAsync(1, Arg.Any<CancellationToken>()).Returns(
        [
            new GitLightRepoDto { Id = 10, ProjectId = 1, Slug = "first", DefaultBranch = "main" },
            new GitLightRepoDto { Id = 20, ProjectId = 1, Slug = "second", DefaultBranch = "main" }
        ]);

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ReadProjectPipelineYamlAsync(
                1, "deploy", ct: TestContext.Current.CancellationToken));

        Assert.Contains("multiple repositories", error.Message, StringComparison.OrdinalIgnoreCase);
        await _cliMock.DidNotReceiveWithAnyArgs().GetBlobAsync(
            string.Empty, string.Empty, string.Empty, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReadProjectConfigAtRevisionAsync_SelectedRepository_UsesExactCommitAndSafePath()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        _gitServiceMock.GetRepositoriesAsync(1, Arg.Any<CancellationToken>()).Returns(
        [
            new GitLightRepoDto { Id = 10, ProjectId = 1, Slug = "first", DefaultBranch = "main" },
            new GitLightRepoDto { Id = 20, ProjectId = 1, Slug = "second", DefaultBranch = "develop" }
        ]);
        var secondPath = Path.Combine(Path.GetTempPath(), "pgs-test-1-second");
        Directory.CreateDirectory(secondPath);
        _gitServiceMock.ResolveDiskPath(1, "second").Returns(secondPath);
        _cliMock.GetBlobAsync(
                secondPath, commit, ".pipeline/configs/apache/site.conf", Arg.Any<CancellationToken>())
            .Returns(new GitLightBlobDto
            {
                Content = "ServerName #{HOST}#\n",
                Path = ".pipeline/configs/apache/site.conf"
            });

        var result = await _sut.ReadProjectConfigAtRevisionAsync(
            1, ".pipeline/configs/apache/site.conf", commit,
            TestContext.Current.CancellationToken, 20);

        Assert.Equal("ServerName #{HOST}#\n", result);
        await _cliMock.Received(1).GetBlobAsync(
            secondPath, commit, ".pipeline/configs/apache/site.conf", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(".pipeline/configs/../secret.conf", null)]
    [InlineData(".pipeline/configs/apache/site.conf", "main")]
    public async Task ReadProjectConfigAtRevisionAsync_UnsafePathOrMutableRevision_IsRejected(
        string path, string? revision)
    {
        revision ??= new string('a', 40);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.ReadProjectConfigAtRevisionAsync(
                1, path, revision, TestContext.Current.CancellationToken));

        await _gitServiceMock.DidNotReceiveWithAnyArgs().GetRepositoriesAsync(
            default, TestContext.Current.CancellationToken);
    }

    // --- WriteProjectPipelineYamlAsync ---

    [Fact]
    public async Task WriteProjectPipelineYamlAsync_NoRepo_ReturnsNoRepo()
    {
        _gitServiceMock.GetRepositoriesAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<GitLightRepoDto>());

        var (outcome, error) = await _sut.WriteProjectPipelineYamlAsync(1, "deploy", "yaml content", "admin", ct: TestContext.Current.CancellationToken);

        // No internal repo is a non-error DB-only fallback, not a git failure.
        Assert.Equal(GitWriteOutcome.NoRepo, outcome);
        Assert.Null(error);
    }

    [Fact]
    public async Task WriteProjectPipelineYamlAsync_ValidRepo_CommitsFile()
    {
        SetupRepoWithDiskPath(1, "my-repo", "main");

        _cliMock.CommitFileAsync(
            Arg.Any<string>(), "main", ".pipeline/deploy.yaml", "yaml content",
            Arg.Any<string>(), "admin", "admin@aetheus", Arg.Any<CancellationToken>())
            .Returns((true, "abc123", (string?)null));

        var (outcome, error) = await _sut.WriteProjectPipelineYamlAsync(1, "deploy", "yaml content", "admin", ct: TestContext.Current.CancellationToken);

        Assert.Equal(GitWriteOutcome.Committed, outcome);
        Assert.Null(error);
    }

    [Fact]
    public async Task WriteProjectPipelineYamlAsync_SelectedBranch_CommitsToSelectedBranch()
    {
        SetupRepoWithDiskPath(1, "my-repo", "main");
        _cliMock.CommitFileAsync(
            Arg.Any<string>(), "develop", ".pipeline/deploy.yaml", "yaml content",
            Arg.Any<string>(), "admin", "admin@aetheus", Arg.Any<CancellationToken>())
            .Returns((true, "abc123", (string?)null));

        var (outcome, _) = await _sut.WriteProjectPipelineYamlAsync(
            1, "deploy", "yaml content", "admin", sourceBranch: "develop", ct: TestContext.Current.CancellationToken);

        Assert.Equal(GitWriteOutcome.Committed, outcome);
        await _cliMock.Received(1).CommitFileAsync(
            Arg.Any<string>(), "develop", ".pipeline/deploy.yaml", "yaml content",
            Arg.Any<string>(), "admin", "admin@aetheus", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteProjectPipelineYamlAsync_CommitFails_ReturnsFailed()
    {
        SetupRepoWithDiskPath(1, "my-repo", "main");

        _cliMock.CommitFileAsync(
            Arg.Any<string>(), "main", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((false, (string?)null, "conflict"));

        var (outcome, error) = await _sut.WriteProjectPipelineYamlAsync(1, "deploy", "yaml", "admin", ct: TestContext.Current.CancellationToken);

        Assert.Equal(GitWriteOutcome.Failed, outcome);
        Assert.Equal("conflict", error);
    }

    [Fact]
    public async Task ApplyProjectPipelineChangeAsync_SameRepoRename_IsOneAtomicCommit()
    {
        SetupRepoWithDiskPath(1, "my-repo", "main");
        _cliMock.CommitFileChangesAsync(
                Arg.Any<string>(), "main",
                Arg.Any<IReadOnlyList<(string RelativePath, string Content)>>(),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), "admin", "admin@aetheus",
                Arg.Any<CancellationToken>())
            .Returns((true, "abc123", (string?)null));

        var (outcome, error) = await _sut.ApplyProjectPipelineChangeAsync(
            new PipelineGitDefinitionLocation(1, "old name", "main"),
            new PipelineGitDefinitionLocation(1, "new name", "main"),
            "name: new name", "admin", ct: TestContext.Current.CancellationToken);

        Assert.Equal(GitWriteOutcome.Committed, outcome);
        Assert.Null(error);
        await _cliMock.Received(1).CommitFileChangesAsync(
            Arg.Any<string>(), "main",
            Arg.Is<IReadOnlyList<(string RelativePath, string Content)>>(files =>
                files.Count == 1 && files[0].RelativePath == ".pipeline/new-name.yaml"),
            Arg.Is<IReadOnlyList<string>>(paths =>
                paths.Count == 1 && paths[0] == ".pipeline/old-name.yaml"),
            Arg.Any<string>(), "admin", "admin@aetheus", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyProjectPipelineChangeAsync_Delete_CommitsYamlRemoval()
    {
        SetupRepoWithDiskPath(1, "my-repo", "main");
        _cliMock.CommitFileChangesAsync(
                Arg.Any<string>(), "main", Arg.Any<IReadOnlyList<(string, string)>>(),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), "admin", "admin@aetheus",
                Arg.Any<CancellationToken>())
            .Returns((true, "abc123", (string?)null));

        var (outcome, _) = await _sut.ApplyProjectPipelineChangeAsync(
            new PipelineGitDefinitionLocation(1, "obsolete", "main"), null, null, "admin", ct: TestContext.Current.CancellationToken);

        Assert.Equal(GitWriteOutcome.Committed, outcome);
        await _cliMock.Received(1).CommitFileChangesAsync(
            Arg.Any<string>(), "main", Arg.Is<IReadOnlyList<(string, string)>>(files => files.Count == 0),
            Arg.Is<IReadOnlyList<string>>(paths =>
                paths.Count == 1 && paths[0] == ".pipeline/obsolete.yaml"),
            Arg.Any<string>(), "admin", "admin@aetheus", Arg.Any<CancellationToken>());
    }

    // --- CopyEnvironmentPipelinesToProjectAsync ---

    [Fact]
    public async Task CopyEnvironmentPipelinesToProjectAsync_NoRepo_ReturnsZero()
    {
        _gitServiceMock.GetRepositoriesAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<GitLightRepoDto>());

        var result = await _sut.CopyEnvironmentPipelinesToProjectAsync(1, "staging", 1, "admin", ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task CopyEnvironmentPipelinesToProjectAsync_NoPipelines_ReturnsZero()
    {
        SetupRepoWithDiskPath(1, "my-repo", "main");
        _pipelineRepoMock.GetPipelinesByEnvironmentAsync(5, Arg.Any<CancellationToken>())
            .Returns(new List<Pipeline>());

        var result = await _sut.CopyEnvironmentPipelinesToProjectAsync(5, "staging", 1, "admin", ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task CopyEnvironmentPipelinesToProjectAsync_WithPipelines_CommitsAndReturnsCount()
    {
        SetupRepoWithDiskPath(1, "my-repo", "main");
        _pipelineRepoMock.GetPipelinesByEnvironmentAsync(5, Arg.Any<CancellationToken>())
            .Returns([
                new Pipeline { Id = 1, Name = "build", YamlDefinition = "steps: []" },
                new Pipeline { Id = 2, Name = "deploy", YamlDefinition = "steps: []" }
            ]);
        _cliMock.CommitFilesAsync(
            Arg.Any<string>(), "main", Arg.Any<IReadOnlyList<(string, string)>>(),
            Arg.Any<string>(), "admin", "admin@aetheus", Arg.Any<CancellationToken>())
            .Returns((true, "abc123", (string?)null));

        var result = await _sut.CopyEnvironmentPipelinesToProjectAsync(5, "staging", 1, "admin", ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task CopyEnvironmentPipelinesToProjectAsync_CommitFails_ReturnsZero()
    {
        SetupRepoWithDiskPath(1, "my-repo", "main");
        _pipelineRepoMock.GetPipelinesByEnvironmentAsync(5, Arg.Any<CancellationToken>())
            .Returns([new Pipeline { Id = 1, Name = "build", YamlDefinition = "steps: []" }]);
        _cliMock.CommitFilesAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<(string, string)>>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((false, (string?)null, "error"));

        var result = await _sut.CopyEnvironmentPipelinesToProjectAsync(5, "staging", 1, "admin", ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, result);
    }

    // --- Helpers ---

    private void SetupRepoWithDiskPath(int projectId, string slug, string defaultBranch)
    {
        _gitServiceMock.GetRepositoriesAsync(projectId, Arg.Any<CancellationToken>())
            .Returns([new GitLightRepoDto
            {
                Id = 1,
                ProjectId = projectId,
                Slug = slug,
                DefaultBranch = defaultBranch,
                IsEmpty = false
            }]);

        // ResolveDiskPath returns a path that exists in tests - use temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"pgs-test-{projectId}-{slug}");
        Directory.CreateDirectory(tempDir);
        _gitServiceMock.ResolveDiskPath(projectId, slug).Returns(tempDir);
    }
}
