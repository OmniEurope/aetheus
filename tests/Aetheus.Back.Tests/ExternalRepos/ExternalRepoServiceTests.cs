// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.ExternalRepos;
using Aetheus.Back.Components.Git;
using Aetheus.Back.Components.Projects;
using Aetheus.Back.Components.ServiceConnections;
using Aetheus.Back.Configuration;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.ExternalRepos;

public sealed class ExternalRepoServiceTests
{
    private readonly IProjectRepository _projects = Substitute.For<IProjectRepository>();
    private readonly IGitRepository _git = Substitute.For<IGitRepository>();
    private readonly IGitLightRepository _light = Substitute.For<IGitLightRepository>();
    private readonly IServiceConnectionRepository _connections = Substitute.For<IServiceConnectionRepository>();
    private readonly IExternalRepoMirrorService _mirror = Substitute.For<IExternalRepoMirrorService>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly IEntityChangeNotifier _notifier = Substitute.For<IEntityChangeNotifier>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero));

    public ExternalRepoServiceTests()
    {
        _light.GetByProjectAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _mirror.TestConnectivityAsync(
                Arg.Any<GitProviderType>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<GitAuthType>(), Arg.Any<GitCredentialPayload?>(), Arg.Any<CancellationToken>())
            .Returns((true, (string?)null));
        _encryption.EncryptValue(Arg.Any<string>()).Returns(call => $"enc:{call.Arg<string>()}");
    }

    [Fact]
    public async Task AttachAsync_FeatureDisabled_IsHiddenAsNotFound()
    {
        var service = Build(enabled: false);

        await Assert.ThrowsAsync<NotFoundException>(() => service.AttachAsync(ValidRequest(), ct: TestContext.Current.CancellationToken));
        await _projects.DidNotReceive().FindProjectAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetForProjectAsync_MissingProjectOrConnection_ReturnsNull()
    {
        var service = Build();
        _projects.FindProjectAsync(1, Arg.Any<CancellationToken>()).Returns((Project?)null);
        _projects.FindProjectAsync(2, Arg.Any<CancellationToken>()).Returns(new Project { Id = 2, GitConnectionId = 9 });
        _git.FindConnectionAsync(9, Arg.Any<CancellationToken>()).Returns((GitConnection?)null);

        Assert.Null(await service.GetForProjectAsync(1, ct: TestContext.Current.CancellationToken));
        Assert.Null(await service.GetForProjectAsync(2, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetForProjectAsync_MapsMirrorAndSshCredentialWithoutExposingSecret()
    {
        var service = Build();
        var connection = new GitConnection
        {
            Id = 9,
            ProjectId = 1,
            ProviderType = GitProviderType.GitLab,
            OwnerOrGroup = "team",
            RepositoryName = "api",
            ServiceConnectionId = 12,
            MirrorStatus = GitMirrorStatus.Ready,
            AutoSyncEnabled = true,
            FetchIntervalMinutes = 30
        };
        _projects.FindProjectAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Project { Id = 1, GitConnectionId = 9, RepositoryUrl = "https://api.example/git/1/api.git" });
        _git.FindConnectionAsync(9, Arg.Any<CancellationToken>()).Returns(connection);
        _light.GetByProjectAsync(1, Arg.Any<CancellationToken>())
            .Returns([new GitInternalRepo { GitConnectionId = 9, DefaultBranch = "develop" }]);
        _connections.FindAsync(12, Arg.Any<CancellationToken>())
            .Returns(new ServiceConnection { Id = 12, Type = ServiceConnectionType.SSH, EncryptedPayload = "secret" });

        var dto = await service.GetForProjectAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.Equal(GitAuthType.Ssh, dto.AuthType);
        Assert.Equal("develop", dto.DefaultBranch);
        Assert.Equal(GitMirrorStatus.Ready, dto.MirrorStatus);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(dto), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttachAsync_MissingProject_ThrowsNotFound()
    {
        _projects.FindProjectAsync(1, Arg.Any<CancellationToken>()).Returns((Project?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => Build().AttachAsync(ValidRequest(), ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AttachAsync_ExistingSource_RejectsExternalOrInternalConflict(bool external)
    {
        var project = new Project { Id = 1, GitConnectionId = external ? 8 : null };
        _projects.FindProjectAsync(1, Arg.Any<CancellationToken>()).Returns(project);
        if (!external)
            _light.GetByProjectAsync(1, Arg.Any<CancellationToken>()).Returns([new GitInternalRepo { Id = 4 }]);

        await Assert.ThrowsAsync<ConflictException>(() => Build().AttachAsync(ValidRequest(), ct: TestContext.Current.CancellationToken));
        await _mirror.DidNotReceive().TestConnectivityAsync(
            Arg.Any<GitProviderType>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<GitAuthType>(), Arg.Any<GitCredentialPayload?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(GitAuthType.HttpsToken, null, null, null, "access token")]
    [InlineData(GitAuthType.Ssh, null, null, "host ssh-ed25519 AAAA", "private key")]
    [InlineData(GitAuthType.Ssh, null, "PRIVATE KEY", null, "known_hosts")]
    public async Task AttachAsync_InvalidCredential_FailsBeforeConnectivity(
        GitAuthType authType, string? token, string? privateKey, string? knownHosts, string expected)
    {
        _projects.FindProjectAsync(1, Arg.Any<CancellationToken>()).Returns(new Project { Id = 1 });
        var request = ValidRequest() with
        {
            AuthType = authType,
            Token = token,
            PrivateKeyPem = privateKey,
            KnownHosts = knownHosts
        };

        var error = await Assert.ThrowsAsync<BadRequestException>(() => Build().AttachAsync(request, ct: TestContext.Current.CancellationToken));

        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
        await _mirror.DidNotReceive().TestConnectivityAsync(
            Arg.Any<GitProviderType>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<GitAuthType>(), Arg.Any<GitCredentialPayload?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AttachAsync_UnreachableRemote_DoesNotPersistPartialSource()
    {
        _projects.FindProjectAsync(1, Arg.Any<CancellationToken>()).Returns(new Project { Id = 1 });
        _mirror.TestConnectivityAsync(
                Arg.Any<GitProviderType>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<GitAuthType>(), Arg.Any<GitCredentialPayload?>(), Arg.Any<CancellationToken>())
            .Returns((false, "authentication failed"));

        var error = await Assert.ThrowsAsync<BadRequestException>(() => Build().AttachAsync(ValidRequest(), ct: TestContext.Current.CancellationToken));

        Assert.Contains("authentication failed", error.Message, StringComparison.Ordinal);
        await _connections.DidNotReceive().AddAsync(Arg.Any<ServiceConnection>(), Arg.Any<CancellationToken>());
        await _git.DidNotReceive().AddConnectionAsync(Arg.Any<GitConnection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AttachAsync_ValidRequest_PersistsCredentialConnectionMirrorAndProject()
    {
        var project = new Project { Id = 1, Name = "API", OrganizationId = 3 };
        _projects.FindProjectAsync(1, Arg.Any<CancellationToken>()).Returns(project);
        _connections.AddAsync(Arg.Any<ServiceConnection>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<ServiceConnection>().Id = 17;
            return Task.CompletedTask;
        });
        _git.AddConnectionAsync(Arg.Any<GitConnection>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<GitConnection>().Id = 23;
            return Task.CompletedTask;
        });
        _mirror.SyncAsync(Arg.Any<GitConnection>(), Arg.Any<GitInternalRepo>(), Arg.Any<CancellationToken>())
            .Returns(GitMirrorStatus.Ready);
        var request = ValidRequest() with
        {
            RepositoryName = "My API_repo.git",
            DefaultBranch = "develop",
            FetchIntervalMinutes = 9000,
            AutoSyncEnabled = false,
            WriteEnabled = true
        };

        var dto = await Build().AttachAsync(request, ct: TestContext.Current.CancellationToken);

        Assert.Equal(23, dto.GitConnectionId);
        Assert.Equal("develop", dto.DefaultBranch);
        Assert.Equal("https://api.example/git/1/my-api-repo-git.git", dto.CloneUrl);
        Assert.Equal(23, project.GitConnectionId);
        Assert.Equal(dto.CloneUrl, project.RepositoryUrl);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, project.UpdatedAt);
        await _connections.Received(1).AddAsync(Arg.Is<ServiceConnection>(c =>
            c.Id == 17 && c.ProjectId == 1 && c.EncryptedPayload.StartsWith("enc:", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        await _git.Received(1).AddConnectionAsync(Arg.Is<GitConnection>(c =>
            c.Id == 23 && c.ServiceConnectionId == 17 && c.FetchIntervalMinutes == 1440 && c.WriteEnabled), Arg.Any<CancellationToken>());
        await _light.Received(1).AddAsync(Arg.Is<GitInternalRepo>(r =>
            r.Slug == "my-api-repo-git" && r.GitConnectionId == 23 && r.DefaultBranch == "develop"), Arg.Any<CancellationToken>());
        await _mirror.Received(1).SyncAsync(Arg.Any<GitConnection>(), Arg.Any<GitInternalRepo>(), Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("Attached", "ExternalRepo", 23, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).BroadcastAsync(ResourceType.Project, 1, EntityChangeOps.Updated, Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task SyncNowAsync_CompleteAttachment_SyncsAndReturnsFreshState()
    {
        var project = new Project { Id = 1, GitConnectionId = 9, RepositoryUrl = "clone" };
        var connection = new GitConnection { Id = 9, ProjectId = 1, ServiceConnectionId = null };
        var mirrorRepo = new GitInternalRepo { Id = 4, GitConnectionId = 9, DefaultBranch = "main" };
        _projects.FindProjectAsync(1, Arg.Any<CancellationToken>()).Returns(project);
        _git.FindConnectionAsync(9, Arg.Any<CancellationToken>()).Returns(connection);
        _light.GetByProjectAsync(1, Arg.Any<CancellationToken>()).Returns([mirrorRepo]);
        _mirror.SyncAsync(connection, mirrorRepo, Arg.Any<CancellationToken>()).Returns(GitMirrorStatus.Ready);

        var dto = await Build().SyncNowAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.Equal(GitAuthType.HttpsToken, dto.AuthType);
        await _git.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _light.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.Received(1).BroadcastAsync(ResourceType.Project, 1, EntityChangeOps.Updated, Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task DetachAsync_FullAttachment_RemovesMirrorConnectionAndCredentialDurably()
    {
        var project = new Project { Id = 1, GitConnectionId = 9, RepositoryUrl = "clone" };
        var connection = new GitConnection { Id = 9, ProjectId = 1, ServiceConnectionId = 12 };
        var mirrorRepo = new GitInternalRepo { Id = 4, GitConnectionId = 9, Slug = "api" };
        var credential = new ServiceConnection { Id = 12 };
        _projects.FindProjectAsync(1, Arg.Any<CancellationToken>()).Returns(project);
        _git.FindConnectionAsync(9, Arg.Any<CancellationToken>()).Returns(connection);
        _light.GetByProjectAsync(1, Arg.Any<CancellationToken>()).Returns([mirrorRepo]);
        _connections.FindAsync(12, Arg.Any<CancellationToken>()).Returns(credential);
        _mirror.ResolveMirrorPath(1, "api").Returns(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var detached = await Build().DetachAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(detached);
        Assert.Null(project.GitConnectionId);
        Assert.Null(project.RepositoryUrl);
        await _light.Received(1).RemoveAsync(mirrorRepo, Arg.Any<CancellationToken>());
        await _git.Received(1).RemoveConnectionAsync(connection, Arg.Any<CancellationToken>());
        await _connections.Received(1).RemoveAsync(credential, Arg.Any<CancellationToken>());
        await _connections.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync("Detached", "ExternalRepo", 9, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DetachAsync_ProjectWithoutAttachment_ReturnsFalseWithoutSideEffects()
    {
        _projects.FindProjectAsync(1, Arg.Any<CancellationToken>()).Returns(new Project { Id = 1 });

        Assert.False(await Build().DetachAsync(1, ct: TestContext.Current.CancellationToken));

        await _projects.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private ExternalRepoService Build(bool enabled = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aetheus:PublicApiBaseUrl"] = "https://api.example"
            })
            .Build();
        return new ExternalRepoService(
            _projects, _git, _light, _connections, _mirror, _encryption, _audit, _notifier,
            Substitute.For<IHttpContextAccessor>(), config,
            Options.Create(new FeatureFlagsOptions { ExternalRepos = enabled }),
            _time, NullLogger<ExternalRepoService>.Instance);
    }

    private static AttachExternalRepoRequest ValidRequest() => new()
    {
        ProjectId = 1,
        ProviderType = GitProviderType.GitHub,
        OwnerOrGroup = "team",
        RepositoryName = "api",
        DefaultBranch = "main",
        AuthType = GitAuthType.HttpsToken,
        Username = "git",
        Token = "token",
        AutoSyncEnabled = true,
        FetchIntervalMinutes = 15
    };
}
