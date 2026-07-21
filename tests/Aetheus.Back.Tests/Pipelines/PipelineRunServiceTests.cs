// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.GitGraph;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Releases;
using Aetheus.Back.Components.VariableLibraries;
using Aetheus.Back.Components.Vaults;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Aetheus.Back.Services.DomainEvents;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class PipelineRunServiceTests
{
    private readonly IPipelineRepository _repoMock = Substitute.For<IPipelineRepository>();
    private readonly IHubContext<PipelineHub> _hubMock = Substitute.For<IHubContext<PipelineHub>>();
    private readonly IVariableLibraryService _varLibMock = Substitute.For<IVariableLibraryService>();
    private readonly IVaultService _vaultMock = Substitute.For<IVaultService>();
    private readonly IReleaseService _releaseServiceMock = Substitute.For<IReleaseService>();
    private readonly ISecretMaskingService _secretMaskingMock = Substitute.For<ISecretMaskingService>();
    private readonly IResourceAuthorizationService _authzMock = Substitute.For<IResourceAuthorizationService>();
    private readonly IAuditService _auditMock = Substitute.For<IAuditService>();
    private readonly ILogger<PipelineRunService> _loggerMock = Substitute.For<ILogger<PipelineRunService>>();
    private readonly IPipelineGitService _pipelineGitMock = Substitute.For<IPipelineGitService>();
    private readonly Aetheus.Back.Components.Artifacts.IArtifactRepository _artifactRepoMock = Substitute.For<Aetheus.Back.Components.Artifacts.IArtifactRepository>();
    private readonly IHttpClientFactory _httpClientFactoryMock = Substitute.For<IHttpClientFactory>();
    private readonly IPipelineVariableResolver _variableResolverMock;
    private readonly IClientProxy _clientProxyMock = Substitute.For<IClientProxy>();
    private readonly PipelineRunService _sut;

    public PipelineRunServiceTests()
    {
        _clientProxyMock
            .SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var clientsMock = Substitute.For<IHubClients>();
        clientsMock.All.Returns(_clientProxyMock);
        clientsMock.Group(Arg.Any<string>()).Returns(_clientProxyMock);
        clientsMock.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(_clientProxyMock);
        _hubMock.Clients.Returns(clientsMock);

        _releaseServiceMock
            .NotifyPipelineRunCompletedAsync(Arg.Any<int>(), Arg.Any<PipelineStatus>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Default: run is active (Running) so AdvanceStageAsync proceeds.
        _repoMock.IsRunStillRunningAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetCompletedStageNamesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _repoMock.GetTerminalStageNamesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _repoMock.GetRunAffinityServerIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((int?)null);
        _repoMock.GetStageProducerServerIdAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((int?)null);
        _repoMock.FindOnlineServerByIdAsync(Arg.Any<int>(), Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns((Server?)null);
        _repoMock.FindCandidateTargetServerIdsAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<OsType>(),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetSuccessfulStepOutputsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<StepOutputProjection>());
        _repoMock.GetTriggeredChildRunIdsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var domainEventsMock = Substitute.For<IDomainEventDispatcher>();
        domainEventsMock
            .DispatchAsync(Arg.Any<Aetheus.Back.Components.Pipelines.Events.PipelineRunCompletedEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _httpClientFactoryMock.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        _pipelineGitMock.ReadProjectPipelineYamlAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        // Real variable resolver wired with mocked dependencies (tests exercise the resolution logic).
        var configMock = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        _variableResolverMock = new PipelineVariableResolver(_varLibMock, _vaultMock, _repoMock, configMock, TimeProvider.System);

        // Encryption pass-through: env-protection round-trips are covered by TaskEnvProtection tests.
        var encryptionMock = Substitute.For<Aetheus.Back.Services.IEncryptionService>();
        encryptionMock.EncryptValue(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        encryptionMock.DecryptValue(Arg.Any<string>()).Returns(ci => ci.Arg<string>());

        var gitGraphMock = Substitute.For<IGitGraphRecorder>();
        gitGraphMock.ResolveRunLinksAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((new List<CommitLinkDto>(), new List<BranchLinkDto>()));

        _sut = new PipelineRunService(
            _repoMock, _pipelineGitMock, _hubMock, _variableResolverMock,
            domainEventsMock,
            _secretMaskingMock, _auditMock,
            _authzMock, _httpClientFactoryMock, _loggerMock,
            TimeProvider.System, gitGraphMock, _artifactRepoMock, encryptionMock,
            Substitute.For<Aetheus.Back.Components.AppMonitoring.IAppDeployEnvProvider>(), configMock,
            new PipelineTemplateResolver(_repoMock));
    }

    // --- TriggerRunAsync ---

    [Fact]
    public async Task PrepareRunAsync_PinnedTemplateSnapshotRemainsOnVersionOneAfterVersionTwoPublished()
    {
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(new Pipeline
        {
            Id = 1,
            Name = "toto",
            YamlDefinition = "name: toto\nextends: ci@1\nstages: []"
        });
        _repoMock.GetPipelineOrganizationIdAsync(1, Arg.Any<CancellationToken>()).Returns(4);
        var template = new PipelineTemplate
        {
            Id = 7,
            Name = "ci",
            OrganizationId = 4,
            LatestVersion = 1,
            Versions =
            [
                new PipelineTemplateVersion
                {
                    TemplateId = 7,
                    Version = 1,
                    YamlContent = "name: ci\nstages:\n  - name: build\n    steps:\n      - name: compile\n        shell: echo v1"
                }
            ]
        };
        _repoMock.FindTemplateByNameAsync("ci", 4, Arg.Any<CancellationToken>()).Returns(template);

        var beforePublication = await _sut.PrepareRunAsync(1, ct: TestContext.Current.CancellationToken);
        template.LatestVersion = 2;
        template.Versions.Add(new PipelineTemplateVersion
        {
            TemplateId = 7,
            Version = 2,
            YamlContent = "name: ci\nstages:\n  - name: build\n    steps:\n      - name: compile\n        shell: echo v2"
        });
        var afterPublication = await _sut.PrepareRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(beforePublication);
        Assert.NotNull(afterPublication);
        Assert.Contains("echo v1", beforePublication.YamlSnapshot, StringComparison.Ordinal);
        Assert.Contains("echo v1", afterPublication.YamlSnapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("echo v2", afterPublication.YamlSnapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRunParametersAsync_ReadsSelectedSourceBranch()
    {
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(new Pipeline
        {
            Id = 1,
            ProjectId = 7,
            Name = "release",
            SourceBranch = "main",
            YamlDefinition = "name: release\nstages: []"
        });
        _pipelineGitMock.ReadProjectPipelineYamlAsync(
                7, "release", Arg.Any<CancellationToken>(), "release/2026.07")
            .Returns("name: release\nparameters:\n  - name: target\nstages: []");

        var parameters = await _sut.GetRunParametersAsync(1, "release/2026.07", ct: TestContext.Current.CancellationToken);

        Assert.Equal("target", Assert.Single(parameters).Name);
        await _pipelineGitMock.Received(1).ReadProjectPipelineYamlAsync(
            7, "release", Arg.Any<CancellationToken>(), "release/2026.07");
    }

    [Fact]
    public async Task TriggerRunAsync_PipelineNotFound_ReturnsNull()
    {
        _repoMock.FindPipelineAsync(99, Arg.Any<CancellationToken>())
            .Returns((Pipeline?)null);

        var result = await _sut.TriggerRunAsync(99, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task TriggerRunAsync_InvalidYaml_IsRejected()
    {
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Bad", YamlDefinition = "{{invalid", Runs = [] });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TriggerRunAsync_MalformedContainerLimit_FailsBeforeRunCreation()
    {
        var yaml = """
            name: deploy
            isolation:
              mode: container
              image: alpine
              cpus: nope
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
            """;
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = yaml, Runs = [] });

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken));

        Assert.Contains("CPU limit", error.Message, StringComparison.OrdinalIgnoreCase);
        await _repoMock.DidNotReceive().AddPipelineRunAsync(
            Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TriggerRunAsync_ValidPipeline_CreatesRun()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
            """;

        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = yaml, Runs = [] });
        _repoMock.AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.TrackPipelineStepRun(Arg.Any<PipelineStepRun>());
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.GetPendingStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Running,
                Pipeline = new Pipeline { Name = "Deploy" },
                StepRuns = []
            });

        var result = await _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(PipelineStatus.Running, result.Status);
        await _repoMock.Received(1).AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>());
        _repoMock.Received(3).TrackPipelineStepRun(Arg.Any<PipelineStepRun>());
    }

    [Fact]
    public async Task TriggerRunAsync_EnvironmentOwnedPipeline_PinsEffectiveProjectCommit()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        var yaml = """
            name: release
            trigger: manual
            stages:
              - name: deploy
                os: linux
                steps:
                  - name: run
                    shell: echo deploy
            """;
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "release", EnvironmentId = 9, YamlDefinition = yaml, Runs = [] });
        _repoMock.GetPipelineProjectIdAsync(Arg.Any<Pipeline>(), Arg.Any<CancellationToken>()).Returns(7);
        _pipelineGitMock.GetHeadCommitShaAsync(7, Arg.Any<CancellationToken>()).Returns(commit);
        _pipelineGitMock.ReadProjectPipelineYamlAtRevisionAsync(7, "release", commit, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        PipelineRun? captured = null;
        _repoMock.AddPipelineRunAsync(Arg.Do<PipelineRun>(run => captured = run), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.GetPendingStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            Status = PipelineStatus.Running,
            Pipeline = new Pipeline { Name = "release" },
            StepRuns = []
        });

        await _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(commit, captured!.CommitHash);
        var variables = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            captured.ResolvedVariablesJson!)!;
        Assert.Equal(commit, variables["BUILD_SOURCEVERSION"]);
        await _pipelineGitMock.Received(1).GetHeadCommitShaAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TriggerRunAsync_SelectedSourceBranch_PinsAndRecordsThatBranch()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        const string yaml = "name: release\ntrigger: manual\nstages: []";
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(new Pipeline
        {
            Id = 1,
            Name = "release",
            ProjectId = 7,
            SourceBranch = "release/2026.07",
            YamlDefinition = yaml,
            Runs = []
        });
        _pipelineGitMock.GetHeadCommitShaAsync(7, Arg.Any<CancellationToken>(), "release/2026.07").Returns(commit);
        _pipelineGitMock.ReadProjectPipelineYamlAtRevisionAsync(7, "release", commit, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        PipelineRun? captured = null;
        _repoMock.AddPipelineRunAsync(Arg.Do<PipelineRun>(run => captured = run), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.GetPendingStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            Status = PipelineStatus.Running,
            Pipeline = new Pipeline { Name = "release" },
            StepRuns = []
        });

        await _sut.TriggerRunAsync(1, new Dictionary<string, string>
        {
            ["AETHEUS_RUN_BRANCH"] = "release/2026.07"
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("release/2026.07", captured!.BranchName);
        await _pipelineGitMock.Received(1).GetHeadCommitShaAsync(7, Arg.Any<CancellationToken>(), "release/2026.07");
    }

    [Fact]
    public async Task TriggerRunAsync_CanonicalYamlSourceBranch_OverridesStaleDatabaseBranch()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        const string yaml = "name: nightly\ntrigger: schedule\nsource_branch: develop\nstages: []";
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(new Pipeline
        {
            Id = 1,
            Name = "nightly",
            ProjectId = 7,
            SourceBranch = "main",
            YamlDefinition = yaml,
            Project = new Project
            {
                Id = 7,
                Name = "Aetheus",
                DefaultBranch = "main",
                OrganizationId = 1
            },
            Runs = []
        });
        _pipelineGitMock.GetHeadCommitShaAsync(7, Arg.Any<CancellationToken>(), "develop").Returns(commit);
        _pipelineGitMock.ReadProjectPipelineYamlAtRevisionAsync(7, "nightly", commit, Arg.Any<CancellationToken>())
            .Returns(yaml);
        PipelineRun? captured = null;
        _repoMock.AddPipelineRunAsync(Arg.Do<PipelineRun>(run => captured = run), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.GetPendingStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            Status = PipelineStatus.Running,
            Pipeline = new Pipeline { Name = "nightly" },
            StepRuns = []
        });

        await _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("develop", captured!.BranchName);
        Assert.Equal(commit, captured.CommitHash);
        var variables = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            captured.ResolvedVariablesJson!)!;
        Assert.Equal("develop", variables["DEFAULT_BRANCH"]);
        Assert.Equal("develop", variables["BUILD_SOURCEBRANCH"]);
    }

    [Fact]
    public async Task TriggerRunAsync_SnapshotResolutionThrows_DoesNotPersistUnpreparedRun()
    {
        // Git resolution happens before authorization. A snapshot that could not be resolved must not
        // create a run row because its exact target servers have not been authorized yet.
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
            """;
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", ProjectId = 5, YamlDefinition = yaml, Runs = [] });
        _repoMock.AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 0,
                PipelineId = 1,
                Status = PipelineStatus.Failed,
                Pipeline = new Pipeline { Name = "Deploy" },
                StepRuns = []
            });

        var pipelineGitMock = Substitute.For<IPipelineGitService>();
        pipelineGitMock.ReadProjectPipelineYamlAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        pipelineGitMock.GetHeadCommitShaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string?>(new InvalidOperationException("git exploded")));

        var httpClientFactoryMock = Substitute.For<IHttpClientFactory>();
        httpClientFactoryMock.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        var domainEventsMock = Substitute.For<IDomainEventDispatcher>();
        var gitGraphMock = Substitute.For<IGitGraphRecorder>();
        var artifactRepoMock = Substitute.For<Aetheus.Back.Components.Artifacts.IArtifactRepository>();
        var encryptionMock = Substitute.For<Aetheus.Back.Services.IEncryptionService>();
        encryptionMock.EncryptValue(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        encryptionMock.DecryptValue(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        var sut = new PipelineRunService(
            _repoMock, pipelineGitMock, _hubMock, _variableResolverMock, domainEventsMock,
            _secretMaskingMock, _auditMock, _authzMock, httpClientFactoryMock, _loggerMock,
            TimeProvider.System, gitGraphMock, artifactRepoMock, encryptionMock,
            Substitute.For<Aetheus.Back.Components.AppMonitoring.IAppDeployEnvProvider>(), config,
            new PipelineTemplateResolver(_repoMock));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken));

        Assert.Equal("git exploded", error.Message);
        await _repoMock.DidNotReceive().AddPipelineRunAsync(
            Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TriggerRunAsync_RehomesProjectRepositoryUrlOntoCloneBase()
    {
        // Regression: the first-stage clone task is created at trigger time, so the project's
        // attach-time mirror URL (frozen on localhost) must be re-homed onto GitLight:CloneBaseUrl -
        // otherwise a containerised agent (where localhost is the container) cannot reach the git
        // server and `git clone` fails with exit 128. See MirrorCloneUrl.Rehome.
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitLight:CloneBaseUrl"] = "http://host.docker.internal:5300"
            })
            .Build();

        var httpClientFactoryMock = Substitute.For<IHttpClientFactory>();
        httpClientFactoryMock.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        var pipelineGitMock = Substitute.For<IPipelineGitService>();
        pipelineGitMock.ReadProjectPipelineYamlAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var domainEventsMock = Substitute.For<IDomainEventDispatcher>();
        var gitGraphMock = Substitute.For<IGitGraphRecorder>();
        var artifactRepoMock = Substitute.For<Aetheus.Back.Components.Artifacts.IArtifactRepository>();
        var encryptionMock = Substitute.For<Aetheus.Back.Services.IEncryptionService>();
        encryptionMock.EncryptValue(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        encryptionMock.DecryptValue(Arg.Any<string>()).Returns(ci => ci.Arg<string>());

        var sut = new PipelineRunService(
            _repoMock, pipelineGitMock, _hubMock, _variableResolverMock, domainEventsMock,
            _secretMaskingMock, _auditMock, _authzMock, httpClientFactoryMock, _loggerMock,
            TimeProvider.System, gitGraphMock, artifactRepoMock, encryptionMock,
            Substitute.For<Aetheus.Back.Components.AppMonitoring.IAppDeployEnvProvider>(), config,
            new PipelineTemplateResolver(_repoMock));

        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    checkout: true
                    shell: dotnet build
            """;
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline
            {
                Id = 1,
                Name = "Deploy",
                YamlDefinition = yaml,
                Runs = [],
                ProjectId = 1,
                Project = new Project
                {
                    Id = 1,
                    Name = "Toto",
                    DefaultBranch = "master",
                    RepositoryUrl = "http://localhost:5300/git/1/toto.git"
                }
            });
        PipelineRun? captured = null;
        _repoMock.AddPipelineRunAsync(Arg.Do<PipelineRun>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.GetPendingStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRun { Id = 1, PipelineId = 1, Status = PipelineStatus.Running, Pipeline = new Pipeline { Name = "Deploy" }, StepRuns = [] });

        await sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        var vars = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(captured!.ResolvedVariablesJson!)!;
        Assert.Equal("http://host.docker.internal:5300/git/1/toto.git", vars["REPOSITORY_URL"]);
        Assert.Equal("http://host.docker.internal:5300/git/1/toto.git", vars["BUILD_REPOSITORY_URI"]);
    }

    // --- F-EXEC-1b: TriggerAutomatedRunAsync (webhook / scheduler, no caller principal) ---

    private const string OwnedYaml = """
        name: deploy
        trigger: manual
        stages:
          - name: build
            agent: linux-01
            steps:
              - name: compile
                shell: dotnet build
        """;

    [Fact]
    public async Task TriggerAutomatedRunAsync_UnownedPipeline_ReturnsNullAndDoesNotCreateRun()
    {
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Legacy", YamlDefinition = OwnedYaml, CreatedByUsername = null, Runs = [] });

        var result = await _sut.TriggerAutomatedRunAsync(1, "Webhook", ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
        await _repoMock.DidNotReceive().AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>());
        await _auditMock.Received(1).LogAsync("BlockedUnownedAutomatedRun", "Pipeline", Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TriggerAutomatedRunAsync_OwnerLacksServerAdmin_ReturnsNullAndDoesNotCreateRun()
    {
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = OwnedYaml, CreatedByUsername = "alice", Runs = [] });
        _repoMock.FindCandidateTargetServerIdsAsync(
                null, null, "linux-01", OsType.Unknown, null, false, Arg.Any<CancellationToken>())
            .Returns([42]);
        _authzMock.HasPermissionAsync("alice", ResourceType.Server, 42, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.TriggerAutomatedRunAsync(1, "Scheduler", ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
        await _repoMock.DidNotReceive().AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TriggerAutomatedRunAsync_OwnerAdministersAllTargets_DelegatesAndCreatesRun()
    {
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = OwnedYaml, CreatedByUsername = "alice", Runs = [] });
        _repoMock.FindCandidateTargetServerIdsAsync(
                null, null, "linux-01", OsType.Unknown, null, false, Arg.Any<CancellationToken>())
            .Returns([42]);
        _authzMock.HasPermissionAsync("alice", ResourceType.Server, 42, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.TrackPipelineStepRun(Arg.Any<PipelineStepRun>());
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.GetPendingStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Running,
                Pipeline = new Pipeline { Name = "Deploy" },
                StepRuns = []
            });

        var result = await _sut.TriggerAutomatedRunAsync(1, "Webhook", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        await _repoMock.Received(1).AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>());
    }

    // --- GetRunsAsync ---

    [Fact]
    public async Task GetRunsAsync_ReturnsMappedPagedResult()
    {
        _repoMock.GetRunsPagedAsync(1, 1, 25, Arg.Any<CancellationToken>())
            .Returns((
                new List<PipelineRunDto>
                {
                    new()
                    {
                        Id = 1, PipelineId = 1, Status = PipelineStatus.Success,
                        PipelineName = "Deploy",
                        Steps = []
                    }
                },
                1));

        var result = await _sut.GetRunsAsync(1, new PaginationRequest(), ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Items);
        Assert.Equal(PipelineStatus.Success, result.Items[0].Status);
        Assert.Equal(1, result.TotalCount);
    }

    // --- GetRunAsync ---

    [Fact]
    public async Task GetRunAsync_Found_ReturnsDto()
    {
        _repoMock.GetRunDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Running,
                Pipeline = new Pipeline { Name = "CI" },
                StepRuns = [new PipelineStepRun { Id = 1, StepName = "build", StageName = "stage1" }]
            });

        var result = await _sut.GetRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("CI", result.PipelineName);
        Assert.Single(result.Steps);
    }

    [Fact]
    public async Task GetRunAsync_NotFound_ReturnsNull()
    {
        _repoMock.GetRunDetailAsync(99, Arg.Any<CancellationToken>())
            .Returns((PipelineRun?)null);

        var result = await _sut.GetRunAsync(99, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    // --- AdvanceStageAsync ---

    [Fact]
    public async Task AdvanceStageAsync_NotAllStepsDone_DoesNotAdvance()
    {
        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(false);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        await _repoMock.DidNotReceive().GetPipelineRunWithPipelineAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_RunNotFound_DoesNothing()
    {
        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns((PipelineRun?)null);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(Arg.Any<int>(), Arg.Any<PipelineStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_AllDone_NoPendingSteps_CompletesWithSuccess()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun { Id = 1, PipelineId = 1, Pipeline = new Pipeline { YamlDefinition = yaml } });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.UpdatePipelineRunStatusAsync(1, PipelineStatus.Success, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(1, PipelineStatus.Success, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_AllDone_HasFailedStep_CompletesWithFailed()
    {
        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 1, StageName = "build", StepName = "compile", Status = TaskExecutionStatus.Failed, RetryCount = 0, ContinueOnError = false }]);
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.HasAnyFailedStepInRunAsync(1, Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_FailedStage_WaitsForSystemCleanupBeforeFinalizing()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: restore
                agent: linux-01
                steps:
                  - name: restore-artifact
                    shell: exit 1
            """;
        var cleanup = new PipelineStepRun
        {
            Id = 2,
            PipelineRunId = 1,
            StageName = PipelineRunService.SystemCleanupStage,
            StepName = "Cleanup",
            Status = TaskExecutionStatus.Pending,
            IsSystem = true
        };
        var run = new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            Status = PipelineStatus.Running,
            ResolvedVariablesJson = "{}",
            Pipeline = new Pipeline { YamlDefinition = yaml }
        };

        _repoMock.AreAllStepsInStageCompletedAsync(1, "restore", Arg.Any<CancellationToken>()).Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "restore", Arg.Any<CancellationToken>()).Returns(
            [new PipelineStepRun { Id = 1, StageName = "restore", StepName = "restore-artifact", Status = TaskExecutionStatus.Failed }]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(run);
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns([cleanup]);
        _repoMock.GetRunAffinityServerIdAsync(1, Arg.Any<CancellationToken>()).Returns(10);
        _repoMock.FindOnlineServerByIdAsync(10, OsType.Unknown, Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01", OsType = OsType.Linux });

        await _sut.AdvanceStageAsync(1, "restore", ct: TestContext.Current.CancellationToken);

        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task => task.Name == "Cleanup"));
        Assert.Equal(TaskExecutionStatus.Assigned, cleanup.Status);
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_CleanupAffinityRunnerOffline_DoesNotDispatchToAnotherRunner()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: restore
                agent: linux-01
                steps:
                  - name: restore-artifact
                    shell: exit 1
            """;
        var cleanup = new PipelineStepRun
        {
            Id = 2,
            PipelineRunId = 1,
            StageName = PipelineRunService.SystemCleanupStage,
            StepName = "Cleanup",
            Status = TaskExecutionStatus.Pending,
            IsSystem = true
        };
        var run = new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            Status = PipelineStatus.Running,
            ResolvedVariablesJson = "{}",
            Pipeline = new Pipeline { YamlDefinition = yaml }
        };

        _repoMock.AreAllStepsInStageCompletedAsync(1, "restore", Arg.Any<CancellationToken>()).Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "restore", Arg.Any<CancellationToken>()).Returns(
            [new PipelineStepRun { Id = 1, StageName = "restore", StepName = "restore-artifact", Status = TaskExecutionStatus.Failed }]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(run);
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns([cleanup]);
        _repoMock.GetRunAffinityServerIdAsync(1, Arg.Any<CancellationToken>()).Returns(10);
        _repoMock.FindOnlineServerByIdAsync(10, OsType.Unknown, Arg.Any<CancellationToken>()).Returns((Server?)null);

        await _sut.AdvanceStageAsync(1, "restore", ct: TestContext.Current.CancellationToken);

        _repoMock.DidNotReceive().TrackTask(Arg.Any<ServerTask>());
        await _repoMock.DidNotReceive().FindOnlineServerByAgentAsync(
            Arg.Any<string>(), Arg.Any<OsType>(), Arg.Any<CancellationToken>());
        Assert.Equal(TaskExecutionStatus.Failed, cleanup.Status);
        await _repoMock.Received(1).AppendRunWarningsAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(warnings => warnings.Any(warning => warning.Contains("requires reconciliation"))),
            Arg.Any<CancellationToken>());
        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_Failure_DispatchesAlwaysStageBeforeFinalizing()
    {
        var yaml = """
            name: qa
            trigger: manual
            stages:
              - name: integration
                agent: linux-01
                steps:
                  - name: test
                    shell: dotnet test
              - name: e2e
                agent: linux-01
                depends_on: [integration]
                steps:
                  - name: browser-test
                    shell: dotnet test
              - name: teardown
                agent: linux-01
                condition: always()
                depends_on: [integration, e2e]
                steps:
                  - name: destroy
                    shell: docker compose down
            """;
        var teardown = new PipelineStepRun
        {
            Id = 2,
            PipelineRunId = 1,
            StageName = "teardown",
            StepName = "destroy",
            Status = TaskExecutionStatus.Pending
        };
        var run = new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            ResolvedVariablesJson = "{}",
            Pipeline = new Pipeline { YamlDefinition = yaml }
        };

        _repoMock.AreAllStepsInStageCompletedAsync(1, "e2e", Arg.Any<CancellationToken>()).Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "e2e", Arg.Any<CancellationToken>()).Returns(
            [new PipelineStepRun { Id = 1, StageName = "e2e", StepName = "browser-test", Status = TaskExecutionStatus.Failed }]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(run);
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns([teardown]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>()).Returns([PipelineRunService.SystemPrepareStage, "integration"]);
        _repoMock.GetTerminalStageNamesAsync(1, Arg.Any<CancellationToken>()).Returns([PipelineRunService.SystemPrepareStage, "integration", "e2e"]);
        _repoMock.HasAnyFailedStepInRunAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01" });

        await _sut.AdvanceStageAsync(1, "e2e", ct: TestContext.Current.CancellationToken);

        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task => task.Name == "destroy"));
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_AfterFailureHandler_FinalizesOriginalFailure()
    {
        var yaml = """
            name: qa
            trigger: manual
            stages:
              - name: teardown
                agent: linux-01
                condition: always()
                steps:
                  - name: destroy
                    shell: docker compose down
            """;
        _repoMock.AreAllStepsInStageCompletedAsync(1, "teardown", Arg.Any<CancellationToken>()).Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "teardown", Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(
            new PipelineRun { Id = 1, PipelineId = 1, Pipeline = new Pipeline { YamlDefinition = yaml } });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.HasAnyFailedStepInRunAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        await _sut.AdvanceStageAsync(1, "teardown", ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(1, PipelineStatus.Success, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_HasPendingSteps_CreatesTasksForNextStage()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: deploy
                agent: linux-01
                execution_role: deploy
                steps:
                  - name: deploy-app
                    shell: deploy.sh
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 2, StageName = "deploy", StepName = "deploy-app", PipelineRunId = 1 }]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01", PipelineRunnerEnabled = true });
        ServerTask? dispatched = null;
        _repoMock.TrackTask(Arg.Do<ServerTask>(task => dispatched = task));
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(dispatched);
        Assert.Equal("deploy-app", dispatched.Name);
        Assert.Equal(10, dispatched.ServerId);
        var dispatchedEnvironment = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            dispatched.EnvironmentVariables)!;
        Assert.Equal("deploy", dispatchedEnvironment["AETHEUS_EXECUTION_ROLE"]);
        await _repoMock.DidNotReceive().FindOnlineDeployTargetAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<OsType>(),
            Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(Arg.Any<int>(), Arg.Any<PipelineStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_ArtifactCollectionUsesSuccessfulStageProducer()
    {
        const string yaml = """
            name: artifacts
            trigger: manual
            stages:
              - name: build
                artifacts:
                  - out/**
                steps:
                  - name: compile
                    shell: dotnet build
            """;
        var run = new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            Status = PipelineStatus.Running,
            ResolvedVariablesJson = "{}",
            Pipeline = new Pipeline { Id = 1, Name = "artifacts", YamlDefinition = yaml }
        };
        var producer = new Server { Id = 22, Name = "producer", OrganizationId = 10, OsType = OsType.Linux };
        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>()).Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(run);
        _repoMock.GetPipelineOrganizationIdAsync(1, Arg.Any<CancellationToken>()).Returns(10);
        _repoMock.GetStageProducerServerIdAsync(1, "build", Arg.Any<CancellationToken>()).Returns(22);
        _repoMock.FindOnlineServerByIdAsync(22, OsType.Unknown, Arg.Any<CancellationToken>()).Returns(producer);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task =>
            task.ServerId == 22 && task.Operation == OperationKind.PipelineCollectArtifacts));
        await _repoMock.DidNotReceive().FindAnyOnlineRunnerAsync(
            Arg.Any<int?>(), Arg.Any<OsType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContinueAfterArtifactCollectionAsync_Success_CompletesRunSuccessfully()
    {
        const string yaml = "name: artifacts\nstages:\n  - name: build\n    steps:\n      - name: compile\n        shell: dotnet build";
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(
            new PipelineRun
            {
                Id = 1,
                PipelineId = 7,
                Pipeline = new Pipeline { Id = 7, YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.HasAnyFailedStepInRunAsync(1, Arg.Any<CancellationToken>()).Returns(false);

        await _sut.ContinueAfterArtifactCollectionAsync(1, TaskExecutionStatus.Success, ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(
            1, PipelineStatus.Success, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TaskExecutionStatus.Failed)]
    [InlineData(TaskExecutionStatus.Cancelled)]
    [InlineData(TaskExecutionStatus.Timeout)]
    public async Task ContinueAfterArtifactCollectionAsync_NonSuccess_FailsRunClosed(
        TaskExecutionStatus collectionStatus)
    {
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns([]);

        await _sut.ContinueAfterArtifactCollectionAsync(1, collectionStatus, ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).AppendRunWarningsAsync(1,
            Arg.Is<IReadOnlyCollection<string>>(warnings =>
                warnings.Single().Contains(collectionStatus.ToString(), StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(
            1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
        await _repoMock.DidNotReceive().GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContinueAfterArtifactCollectionAsync_NonSuccess_WaitsForSystemCleanupBeforeFinalizing()
    {
        const string yaml = "name: artifacts\nstages:\n  - name: build\n    steps:\n      - name: compile\n        shell: dotnet build";
        var cleanup = new PipelineStepRun
        {
            Id = 2,
            PipelineRunId = 1,
            StageName = PipelineRunService.SystemCleanupStage,
            StepName = "Cleanup",
            Status = TaskExecutionStatus.Pending,
            IsSystem = true
        };
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns([cleanup]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(
            new PipelineRun
            {
                Id = 1,
                PipelineId = 7,
                Status = PipelineStatus.Running,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { Id = 7, YamlDefinition = yaml }
            });
        _repoMock.GetRunAffinityServerIdAsync(1, Arg.Any<CancellationToken>()).Returns(10);
        _repoMock.FindOnlineServerByIdAsync(10, OsType.Unknown, Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01", OsType = OsType.Linux });

        await _sut.ContinueAfterArtifactCollectionAsync(
            1, TaskExecutionStatus.Failed, TestContext.Current.CancellationToken);

        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task => task.Name == "Cleanup"));
        Assert.Equal(TaskExecutionStatus.Assigned, cleanup.Status);
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(
            1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_Dispatch_FlipsStepRunToAssigned_GuardsAgainstDoubleDispatch()
    {
        // 0-c regression: a dispatched step-run must leave Pending the instant its task is tracked.
        // Otherwise an orchestration tick (sibling completion / TaskTimeoutService) fired before the
        // agent claims the task re-selects the still-Pending step via GetPendingStepRunsAsync (which
        // filters Status == Pending) and dispatches a SECOND task - duplicate Create Release /
        // Collect Artifacts, wasted agent time. Asserting Assigned proves the real query excludes it.
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: deploy
                agent: linux-01
                steps:
                  - name: deploy-app
                    shell: deploy.sh
            """;

        var pendingStep = new PipelineStepRun
        {
            Id = 2,
            StageName = "deploy",
            StepName = "deploy-app",
            PipelineRunId = 1,
            Status = TaskExecutionStatus.Pending
        };

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([pendingStep]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01" });
        _repoMock.TrackTask(Arg.Any<ServerTask>());
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Assigned, pendingStep.Status);
        Assert.NotNull(pendingStep.Task);
    }

    [Fact]
    public async Task AdvanceStageAsync_MaxParallelOne_DispatchesOnlyOneJobOfGroup()
    {
        // S-TECH-56: a stage group with strategy.max_parallel=1 must dispatch one job at a time.
        var yaml = """
            name: fanout
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: fanout
                agent: linux-01
                depends_on: [build]
                strategy:
                  max_parallel: 1
                jobs:
                  - name: jobA
                    steps:
                      - name: a
                        shell: echo a
                  - name: jobB
                    steps:
                      - name: b
                        shell: echo b
                  - name: jobC
                    steps:
                      - name: c
                        shell: echo c
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns(
            [
                new PipelineStepRun { Id = 2, StageName = "jobA", StepName = "a", PipelineRunId = 1 },
                new PipelineStepRun { Id = 3, StageName = "jobB", StepName = "b", PipelineRunId = 1 },
                new PipelineStepRun { Id = 4, StageName = "jobC", StepName = "c", PipelineRunId = 1 }
            ]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.GetActiveStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01" });
        _repoMock.TrackTask(Arg.Any<ServerTask>());
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        // Only one of the three jobs is dispatched; the run is NOT failed by the throttle.
        _repoMock.Received(1).TrackTask(Arg.Any<ServerTask>());
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_MaxParallelOne_JobInFlight_ThrottlesWithoutFailing()
    {
        // One job already in flight + max_parallel=1 ⇒ no new dispatch, run stays alive (not failed).
        var yaml = """
            name: fanout
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: fanout
                agent: linux-01
                depends_on: [build]
                strategy:
                  max_parallel: 1
                jobs:
                  - name: jobA
                    steps:
                      - name: a
                        shell: echo a
                  - name: jobB
                    steps:
                      - name: b
                        shell: echo b
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Running,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 3, StageName = "jobB", StepName = "b", PipelineRunId = 1 }]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        // jobA is currently running - fills the single max_parallel slot.
        _repoMock.GetActiveStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns(["jobA"]);
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01" });
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        _repoMock.DidNotReceive().TrackTask(Arg.Any<ServerTask>());
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_ReleaseStep_DispatchesCreateReleaseOperation()
    {
        // S-TECH-40: the backend leg of the `type: release` step - it must dispatch a
        // PipelineCreateRelease operation carrying the resolved version and project/run context.
        // (The agent-side execution of that operation still requires a live deployment to verify.)
        var yaml = """
            name: ship
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: ship
                agent: linux-01
                steps:
                  - name: tag-release
                    type: release
                    version: "2.5.0"
                    changelog: true
                    deployed: true
                    artifact: app-package
                    artifact_source_pipeline: ci
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                CommitHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                BranchName = "main",
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { Id = 1, ProjectId = 5, YamlDefinition = yaml }
            });
        var sourcePipeline = new Pipeline { Id = 2, ProjectId = 5, Name = "ci", YamlDefinition = "name: ci\nstages: []" };
        _repoMock.FindTriggeredRunIdByPipelineNameAsync(1, "ci", Arg.Any<CancellationToken>()).Returns(8);
        _repoMock.GetPipelineRunWithPipelineAsync(8, Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 8,
            PipelineId = 2,
            CommitHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Status = PipelineStatus.Success,
            Pipeline = sourcePipeline
        });
        _artifactRepoMock.FindRunArtifactByNameAsync(8, "app-package", Arg.Any<CancellationToken>())
            .Returns(new PipelineArtifact
            {
                Id = 77,
                PipelineRunId = 8,
                ProjectId = 5,
                Name = "app-package",
                Sha256 = new string('a', 64)
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 2, StageName = "ship", StepName = "tag-release", PipelineRunId = 1 }]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.GetActiveStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineProjectIdAsync(Arg.Any<Pipeline>(), Arg.Any<CancellationToken>())
            .Returns(5);
        _repoMock.GetProjectReleasePatternAsync(5, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01" });
        ServerTask? trackedTask = null;
        _repoMock.When(repository => repository.TrackTask(Arg.Any<ServerTask>()))
            .Do(call => trackedTask = call.Arg<ServerTask>());
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(trackedTask);
        Assert.Equal("tag-release", trackedTask.Name);
        Assert.Equal(OperationKind.PipelineCreateRelease, trackedTask.Operation);
        Assert.Equal("2.5.0", trackedTask.Command);
        Assert.Equal(10, trackedTask.ServerId);
        var releaseEnvironment = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            trackedTask.EnvironmentVariables);
        Assert.NotNull(releaseEnvironment);
        Assert.Equal("8", releaseEnvironment["AETHEUS_RELEASE_ARTIFACT_RUN_ID"]);
        Assert.Equal("true", releaseEnvironment["AETHEUS_RELEASE_DEPLOYED"]);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", releaseEnvironment["AETHEUS_RELEASE_COMMIT"]);
        Assert.Equal("main", releaseEnvironment["AETHEUS_RELEASE_BRANCH"]);
    }

    [Fact]
    public async Task AdvanceStageAsync_DeployRelease_SubstitutesRollbackSelectorBeforeResolvingArtifact()
    {
        var yaml = """
            name: rollback
            trigger: manual
            stages:
              - name: rollback
                agent: linux-01
                steps:
                  - name: redeploy
                    type: deploy
                    app: rollbacklocal
                    release: "$(ROLLBACK_RELEASE)"
                    health_url: http://127.0.0.1:18080/health/ready
            """;
        var run = new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            AdditionalVariablesJson = "{\"ROLLBACK_RELEASE\":\"4\"}",
            Pipeline = new Pipeline { Id = 1, ProjectId = 7, YamlDefinition = yaml }
        };

        _repoMock.AreAllStepsInStageCompletedAsync(1, PipelineRunService.SystemPrepareStage, Arg.Any<CancellationToken>()).Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, PipelineRunService.SystemPrepareStage, Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(run);
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns(
            [new PipelineStepRun { Id = 2, StageName = "rollback", StepName = "redeploy", PipelineRunId = 1 }]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>()).Returns([PipelineRunService.SystemPrepareStage]);
        _repoMock.GetActiveStageNamesAsync(1, Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetPipelineProjectIdAsync(run.Pipeline, Arg.Any<CancellationToken>()).Returns(7);
        _repoMock.FindOnlineDeployTargetAsync(null, null, "linux-01", Arg.Any<OsType>(), null, Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01", DeploymentTargetAvailable = true });
        _artifactRepoMock.FindReleaseArtifactSelectionAsync(7, "4", Arg.Any<CancellationToken>())
            .Returns(new ReleaseArtifactSelection(
                new PipelineArtifact { Id = 42, Name = "release", FilePath = "7/1/1/release.zip" }, 4));
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, PipelineRunService.SystemPrepareStage, ct: TestContext.Current.CancellationToken);

        await _artifactRepoMock.Received(1).FindReleaseArtifactSelectionAsync(7, "4", Arg.Any<CancellationToken>());
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task =>
            task.Operation == OperationKind.PipelineDeploy
            && task.Command == "rollbacklocal"
            && task.EnvironmentVariables.Contains("\"AETHEUS_DEPLOY_ARTIFACT_ID\":\"42\"", StringComparison.Ordinal)
            && task.EnvironmentVariables.Contains("\"AETHEUS_DEPLOY_RELEASE_ID\":\"4\"", StringComparison.Ordinal)
            && task.EnvironmentVariables.Contains("AETHEUS_DEPLOY_HEALTH_URL", StringComparison.Ordinal)
            && task.EnvironmentVariables.Contains("health/ready", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AdvanceStageAsync_ReportSteps_DispatchTypedOperationsWithBudgets()
    {
        var yaml = """
            name: quality
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: quality
                agent: linux-01
                steps:
                  - name: substitute-config
                    type: substitute
                    target_files: ["appsettings.json"]
                  - name: publish-coverage
                    type: coverage
                    target_files: ["coverage-merged/Cobertura.xml"]
                    min_coverage: 75
                  - name: publish-lint
                    type: lint
                    target_files: ["reports/lint.sarif"]
                  - name: publish-complexity
                    type: complexity
                    max_complexity: 12
            """;
        ArrangeAdvanceToStage(yaml, "quality",
            new PipelineStepRun { Id = 2, StageName = "quality", StepName = "substitute-config", PipelineRunId = 1 },
            new PipelineStepRun { Id = 3, StageName = "quality", StepName = "publish-coverage", PipelineRunId = 1 },
            new PipelineStepRun { Id = 4, StageName = "quality", StepName = "publish-lint", PipelineRunId = 1 },
            new PipelineStepRun { Id = 5, StageName = "quality", StepName = "publish-complexity", PipelineRunId = 1 });

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t =>
            t.Name == "substitute-config" && t.Operation == OperationKind.PipelineSubstituteVariables
            && t.Command.Contains("appsettings.json", StringComparison.Ordinal)));
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t =>
            t.Name == "publish-coverage" && t.Operation == OperationKind.PipelinePublishCoverage
            && t.Command.Contains("Cobertura.xml", StringComparison.Ordinal)
            && t.EnvironmentVariables.Contains("AETHEUS_MIN_COVERAGE", StringComparison.Ordinal)
            && t.EnvironmentVariables.Contains("75", StringComparison.Ordinal)));
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t =>
            t.Name == "publish-lint" && t.Operation == OperationKind.PipelinePublishLint
            && t.Command.Contains("lint.sarif", StringComparison.Ordinal)));
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t =>
            t.Name == "publish-complexity" && t.Operation == OperationKind.PipelinePublishComplexity
            && t.EnvironmentVariables.Contains("AETHEUS_MAX_COMPLEXITY", StringComparison.Ordinal)
            && t.EnvironmentVariables.Contains("12", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AdvanceStageAsync_ApacheAndCertbotSteps_DispatchValidatedTypedOperations()
    {
        var yaml = """
            name: expose
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: expose
                agent: linux-01
                steps:
                  - name: proxy
                    type: apache-proxy
                    server_name: app.example.com
                    upstream: http://127.0.0.1:8080
                  - name: tls
                    type: certbot
                    domains: "app.example.com, www.example.com"
                    email: admin@example.com
            """;
        ArrangeAdvanceToStage(yaml, "expose",
            new PipelineStepRun { Id = 2, StageName = "expose", StepName = "proxy", PipelineRunId = 1 },
            new PipelineStepRun { Id = 3, StageName = "expose", StepName = "tls", PipelineRunId = 1 });

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t =>
            t.Name == "proxy" && t.Operation == OperationKind.ApacheConfigureProxy
            && t.Command == "app.example.com.conf"
            && t.EnvironmentVariables.Contains("AETHEUS_APACHE_CONFIG_B64", StringComparison.Ordinal)));
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t =>
            t.Name == "tls" && t.Operation == OperationKind.CertbotObtain
            && t.Command == "app.example.com"
            && t.EnvironmentVariables.Contains("www.example.com", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AdvanceStageAsync_InvalidApacheAndCertbotInputs_FailSynchronouslyWithoutDispatch()
    {
        var yaml = """
            name: expose
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: expose
                agent: linux-01
                steps:
                  - name: proxy
                    type: apache-proxy
                    server_name: "bad/domain"
                    upstream: ftp://example.com
                  - name: tls
                    type: certbot
                    domains: "bad/domain"
                    email: not-an-email
            """;
        var proxy = new PipelineStepRun { Id = 2, StageName = "expose", StepName = "proxy", PipelineRunId = 1 };
        var tls = new PipelineStepRun { Id = 3, StageName = "expose", StepName = "tls", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "expose", proxy, tls);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Failed, proxy.Status);
        Assert.Equal(TaskExecutionStatus.Failed, tls.Status);
        _repoMock.DidNotReceive().TrackTask(Arg.Any<ServerTask>());
        await _repoMock.Received().AppendRunWarningsAsync(1,
            Arg.Is<IReadOnlyCollection<string>>(w => w.Any(x => x.Contains("Apache-proxy", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>());
        await _repoMock.Received().AppendRunWarningsAsync(1,
            Arg.Is<IReadOnlyCollection<string>>(w => w.Any(x => x.Contains("Certbot", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_DeployArtifact_DispatchesOnlyToDeploymentTarget()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: deploy
                agent: prod-01
                steps:
                  - name: deploy-app
                    type: deploy
                    app: web-api
                    artifact: package
                    compose: deploy/docker-compose.yml
                    health_timeout_seconds: 45
            """;
        ArrangeAdvanceToStage(yaml, "deploy",
            new PipelineStepRun { Id = 2, StageName = "deploy", StepName = "deploy-app", PipelineRunId = 1 });
        _artifactRepoMock.FindRunArtifactByNameAsync(1, "package", Arg.Any<CancellationToken>())
            .Returns(new PipelineArtifact { Id = 42, PipelineRunId = 1, Name = "package" });

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t =>
            t.Name == "deploy-app" && t.ServerId == 10 && t.Operation == OperationKind.PipelineDeploy
            && t.Command == "web-api"
            && t.EnvironmentVariables.Contains("AETHEUS_DEPLOY_ARTIFACT_ID", StringComparison.Ordinal)
            && t.EnvironmentVariables.Contains("deploy/docker-compose.yml", StringComparison.Ordinal)
            && t.EnvironmentVariables.Contains("45", StringComparison.Ordinal)));
        await _repoMock.Received(1).FindOnlineDeployTargetAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), "prod-01", Arg.Any<OsType>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_RestoreArtifacts_UsesSuccessfulSiblingRunFromDirectParent()
    {
        var yaml = """
            name: qa
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: restore
                agent: qa-01
                steps:
                  - name: restore-package
                    type: restore-artifacts
                    artifact: app-package
                    artifact_source_pipeline: ci
            """;
        var step = new PipelineStepRun { Id = 2, StageName = "restore", StepName = "restore-package", PipelineRunId = 1 };
        ArrangeAdvanceToStageWithVariables(yaml, "restore", "{\"UPSTREAM_RUN_ID\":\"9\"}", step);
        var currentPipeline = new Pipeline { Id = 1, ProjectId = 5, YamlDefinition = yaml };
        var sourcePipeline = new Pipeline { Id = 2, ProjectId = 5, YamlDefinition = "name: ci\nstages: []" };
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                CommitHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                AdditionalVariablesJson = "{\"UPSTREAM_RUN_ID\":\"9\"}",
                ResolvedVariablesJson = "{\"UPSTREAM_RUN_ID\":\"9\"}",
                Pipeline = currentPipeline
            });
        _repoMock.GetPipelineRunWithPipelineAsync(8, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 8,
                PipelineId = 2,
                CommitHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                Status = PipelineStatus.Success,
                Pipeline = sourcePipeline
            });
        _repoMock.FindTriggeredRunIdByPipelineNameAsync(9, "ci", Arg.Any<CancellationToken>()).Returns(8);
        _repoMock.GetPipelineProjectIdAsync(currentPipeline, Arg.Any<CancellationToken>()).Returns(5);
        _repoMock.GetPipelineProjectIdAsync(sourcePipeline, Arg.Any<CancellationToken>()).Returns(5);
        _artifactRepoMock.FindRunArtifactByNameAsync(8, "app-package", Arg.Any<CancellationToken>())
            .Returns(new PipelineArtifact
            {
                Id = 77,
                PipelineRunId = 8,
                ProjectId = 5,
                Name = "app-package",
                Sha256 = new string('a', 64)
            });

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Assigned, step.Status);
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t =>
            t.Operation == OperationKind.PipelineRestoreArtifacts && t.Command == "app-package"
            && t.EnvironmentVariables.Contains("77", StringComparison.Ordinal)
            && t.EnvironmentVariables.Contains("AETHEUS_RESTORE_ARTIFACT_ID", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AdvanceStageAsync_RestoreArtifacts_TopLevelFastRunUsesExactCommitArtifact()
    {
        const string commit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var yaml = """
            name: fast
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: checkout
                    shell: git status
              - name: restore
                agent: linux-01
                steps:
                  - name: restore-package
                    type: restore-artifacts
                    artifact: app-package
                    artifact_source_pipeline: ci
            """;
        var step = new PipelineStepRun { Id = 2, StageName = "restore", StepName = "restore-package", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "restore", step);
        var pipeline = new Pipeline { Id = 1, ProjectId = 5, Name = "fast", YamlDefinition = yaml };
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            CommitHash = commit,
            ResolvedVariablesJson = "{}",
            Pipeline = pipeline
        });
        _repoMock.GetPipelineProjectIdAsync(pipeline, Arg.Any<CancellationToken>()).Returns(5);
        _artifactRepoMock.FindSuccessfulPipelineArtifactByCommitAsync(
                5, "ci", commit, "app-package", Arg.Any<CancellationToken>())
            .Returns(new PipelineArtifact
            {
                Id = 88,
                PipelineRunId = 7,
                ProjectId = 5,
                Name = "app-package",
                Sha256 = new string('b', 64)
            });

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Assigned, step.Status);
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task =>
            task.Operation == OperationKind.PipelineRestoreArtifacts
            && task.EnvironmentVariables.Contains("88", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AdvanceStageAsync_RestoreArtifacts_ReleaseUsesRetainedArtifactAndSafeTargetDirectory()
    {
        var yaml = """
            name: rollback-qa
            trigger: manual
            stages:
              - name: restore
                agent: linux-01
                steps:
                  - name: restore-n-minus-one
                    type: restore-artifacts
                    release: previous-deployed
                    target_directory: .nminus1
            """;
        var step = new PipelineStepRun { Id = 2, StageName = "restore", StepName = "restore-n-minus-one", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "restore", step);
        var pipeline = new Pipeline { Id = 1, ProjectId = 5, Name = "rollback-qa", YamlDefinition = yaml };
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            CommitHash = "cccccccccccccccccccccccccccccccccccccccc",
            ResolvedVariablesJson = "{}",
            Pipeline = pipeline
        });
        _repoMock.GetPipelineProjectIdAsync(pipeline, Arg.Any<CancellationToken>()).Returns(5);
        _artifactRepoMock.FindPreviousDeployedReleaseArtifactAsync(5, "cccccccccccccccccccccccccccccccccccccccc", Arg.Any<CancellationToken>())
            .Returns(new PipelineArtifact
            {
                Id = 99,
                PipelineRunId = 7,
                ProjectId = 5,
                Name = "BuildArtifacts-artifacts",
                Sha256 = new string('d', 64)
            });

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Assigned, step.Status);
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task =>
            task.Operation == OperationKind.PipelineRestoreArtifacts
            && task.EnvironmentVariables.Contains("99", StringComparison.Ordinal)
            && task.EnvironmentVariables.Contains("AETHEUS_RESTORE_TARGET_DIR", StringComparison.Ordinal)
            && task.EnvironmentVariables.Contains(".nminus1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AdvanceStageAsync_RestoreArtifacts_MissingPreviousDeployed_AllowsExplicitBootstrap()
    {
        var yaml = """
            name: rollback-qa
            trigger: manual
            stages:
              - name: restore
                agent: linux-01
                steps:
                  - name: restore-n-minus-one
                    type: restore-artifacts
                    release: previous-deployed
                    target_directory: .nminus1
                    allow_missing: true
            """;
        var step = new PipelineStepRun { Id = 2, StageName = "restore", StepName = "restore-n-minus-one", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "restore", step);
        var pipeline = new Pipeline { Id = 1, ProjectId = 5, Name = "rollback-qa", YamlDefinition = yaml };
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            CommitHash = "dddddddddddddddddddddddddddddddddddddddd",
            ResolvedVariablesJson = "{}",
            Pipeline = pipeline
        });
        _repoMock.GetPipelineProjectIdAsync(pipeline, Arg.Any<CancellationToken>()).Returns(5);
        _artifactRepoMock.FindPreviousDeployedReleaseArtifactAsync(5, "dddddddddddddddddddddddddddddddddddddddd", Arg.Any<CancellationToken>())
            .Returns((PipelineArtifact?)null);
        _artifactRepoMock.HasPreviousDeployedRollbackContractReleaseAsync(
            5, "dddddddddddddddddddddddddddddddddddddddd", Arg.Any<CancellationToken>()).Returns(false);
        _repoMock.AppendRunWarningsAsync(1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Success, step.Status);
        _repoMock.DidNotReceive().TrackTask(Arg.Is<ServerTask>(task =>
            task.Operation == OperationKind.PipelineRestoreArtifacts));
        await _repoMock.Received(1).AppendRunWarningsAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(warnings => warnings.Any(message => message.Contains("explicit bootstrap mode", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_RestoreArtifacts_MissingArtifactAfterContractRelease_FailsClosed()
    {
        var yaml = """
            name: rollback-qa
            trigger: manual
            stages:
              - name: restore
                agent: linux-01
                steps:
                  - name: restore-n-minus-one
                    type: restore-artifacts
                    release: latest-published
                    target_directory: .nminus1
                    allow_missing: true
            """;
        var step = new PipelineStepRun { Id = 2, StageName = "restore", StepName = "restore-n-minus-one", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "restore", step);
        var pipeline = new Pipeline { Id = 1, ProjectId = 5, Name = "rollback-qa", YamlDefinition = yaml };
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            CommitHash = "ffffffffffffffffffffffffffffffffffffffff",
            ResolvedVariablesJson = "{}",
            Pipeline = pipeline
        });
        _repoMock.GetPipelineProjectIdAsync(pipeline, Arg.Any<CancellationToken>()).Returns(5);
        _artifactRepoMock.FindReleaseArtifactAsync(5, "latest-published", Arg.Any<CancellationToken>())
            .Returns((PipelineArtifact?)null);
        _artifactRepoMock.HasPublishedRollbackContractReleaseAsync(5, Arg.Any<CancellationToken>()).Returns(true);
        _repoMock.AppendRunWarningsAsync(1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Failed, step.Status);
        _repoMock.DidNotReceive().TrackTask(Arg.Is<ServerTask>(task =>
            task.Operation == OperationKind.PipelineRestoreArtifacts));
        await _repoMock.Received(1).AppendRunWarningsAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(warnings => warnings.Any(message => message.Contains("has no retained artifact", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_RestoreArtifacts_PublishedMetadataWithoutRetainedArtifact_AllowsExplicitBootstrap()
    {
        var yaml = """
            name: rollback-qa
            trigger: manual
            stages:
              - name: restore
                agent: linux-01
                steps:
                  - name: restore-n-minus-one
                    type: restore-artifacts
                    release: latest-published
                    target_directory: .nminus1
                    allow_missing: true
            """;
        var step = new PipelineStepRun { Id = 2, StageName = "restore", StepName = "restore-n-minus-one", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "restore", step);
        var pipeline = new Pipeline { Id = 1, ProjectId = 5, Name = "rollback-qa", YamlDefinition = yaml };
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            CommitHash = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            ResolvedVariablesJson = "{}",
            Pipeline = pipeline
        });
        _repoMock.GetPipelineProjectIdAsync(pipeline, Arg.Any<CancellationToken>()).Returns(5);
        _artifactRepoMock.FindReleaseArtifactAsync(5, "latest-published", Arg.Any<CancellationToken>())
            .Returns((PipelineArtifact?)null);
        _artifactRepoMock.HasPublishedRollbackContractReleaseAsync(5, Arg.Any<CancellationToken>()).Returns(false);
        _repoMock.AppendRunWarningsAsync(1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Success, step.Status);
        _repoMock.DidNotReceive().TrackTask(Arg.Is<ServerTask>(task =>
            task.Operation == OperationKind.PipelineRestoreArtifacts));
        await _repoMock.Received(1).AppendRunWarningsAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(warnings => warnings.Any(message => message.Contains("explicit bootstrap mode", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "package", "invalid or missing 'app'")]
    [InlineData("web-api", "", "neither 'artifact'")]
    [InlineData("web-api", "missing", "could not resolve")]
    public async Task AdvanceStageAsync_InvalidDeployDefinitions_FailWithoutCreatingTask(
        string app, string artifact, string expectedWarning)
    {
        var yaml = $$"""
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: deploy
                agent: prod-01
                steps:
                  - name: deploy-app
                    type: deploy
                    app: "{{app}}"
                    artifact: "{{artifact}}"
            """;
        var step = new PipelineStepRun { Id = 2, StageName = "deploy", StepName = "deploy-app", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "deploy", step);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Failed, step.Status);
        _repoMock.DidNotReceive().TrackTask(Arg.Any<ServerTask>());
        await _repoMock.Received().AppendRunWarningsAsync(1,
            Arg.Is<IReadOnlyCollection<string>>(warnings => warnings.Any(w => w.Contains(expectedWarning, StringComparison.OrdinalIgnoreCase))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_ProtectedEnvironment_CreatesApprovalAndPausesBeforeDispatch()
    {
        var yaml = EnvironmentStageYaml();
        ArrangeAdvanceToStage(yaml, "ship",
            new PipelineStepRun { Id = 2, StageName = "ship", StepName = "publish", PipelineRunId = 1 });
        _repoMock.FindEnvironmentByNameAsync("prod", Arg.Any<CancellationToken>())
            .Returns(new Aetheus.Back.Data.Entities.Environment { Id = 6, Name = "prod", RequireApproval = true });
        _repoMock.GetApprovalsAsync(1, Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetPipelineIdForRunAsync(1, Arg.Any<CancellationToken>()).Returns(12);
        _repoMock.TryTransitionPipelineRunStatusAsync(
                1, PipelineStatus.Running, PipelineStatus.WaitingForApproval, Arg.Any<CancellationToken>())
            .Returns(true);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).AddApprovalAsync(Arg.Is<PipelineApproval>(approval =>
            approval.PipelineRunId == 1 && approval.StageName == "ship" && approval.EnvironmentId == 6),
            Arg.Any<CancellationToken>());
        await _repoMock.Received(1).TryTransitionPipelineRunStatusAsync(
            1, PipelineStatus.Running, PipelineStatus.WaitingForApproval, Arg.Any<CancellationToken>());
        _repoMock.DidNotReceive().TrackTask(Arg.Any<ServerTask>());
    }

    [Fact]
    public async Task AdvanceStageAsync_OptionalUnsupportedEnvironmentChecks_DoNotBlockDispatch()
    {
        var yaml = EnvironmentStageYaml();
        var step = new PipelineStepRun { Id = 2, StageName = "ship", StepName = "publish", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "ship", step);
        ArrangeEnvironment(
            new EnvironmentCheck { Name = "template", Type = EnvironmentCheckType.TemplateRequirement, IsRequired = false },
            new EnvironmentCheck { Name = "status", Type = EnvironmentCheckType.StatusCheck, IsRequired = false },
            new EnvironmentCheck { Name = "optional-unsafe", Type = EnvironmentCheckType.RestCallback, IsRequired = false, Configuration = "{\"url\":\"http://127.0.0.1\"}" });

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Assigned, step.Status);
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task => task.Name == "publish" && task.ServerId == 10));
    }

    [Fact]
    public async Task AdvanceStageAsync_NonLoopbackDeployHealthUrl_FailsWithoutCreatingTask()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: deploy
                agent: prod-01
                steps:
                  - name: deploy-app
                    type: deploy
                    app: web-api
                    artifact: package
                    health_url: http://10.0.0.10/health/ready
            """;
        var step = new PipelineStepRun { Id = 2, StageName = "deploy", StepName = "deploy-app", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "deploy", step);
        _artifactRepoMock.FindRunArtifactByNameAsync(1, "package", Arg.Any<CancellationToken>())
            .Returns(new PipelineArtifact { Id = 42, PipelineRunId = 1, Name = "package" });

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Failed, step.Status);
        _repoMock.DidNotReceive().TrackTask(Arg.Any<ServerTask>());
        await _repoMock.Received().AppendRunWarningsAsync(1,
            Arg.Is<IReadOnlyCollection<string>>(warnings => warnings.Any(w =>
                w.Contains("loopback HTTP(S) URL", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(EnvironmentCheckType.TemplateRequirement)]
    [InlineData(EnvironmentCheckType.StatusCheck)]
    public async Task AdvanceStageAsync_UnsupportedRequiredEnvironmentCheck_FailsClosed(EnvironmentCheckType type)
    {
        var yaml = EnvironmentStageYaml();
        var step = new PipelineStepRun { Id = 2, StageName = "ship", StepName = "publish", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "ship", step);
        ArrangeEnvironment(new EnvironmentCheck { Name = "unsupported", Type = type, IsRequired = true });

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Failed, step.Status);
        _repoMock.DidNotReceive().TrackTask(Arg.Any<ServerTask>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    public async Task AdvanceStageAsync_MissingRestEnvironmentCheckConfiguration_FailsClosed(string? configuration)
    {
        var yaml = EnvironmentStageYaml();
        var step = new PipelineStepRun { Id = 2, StageName = "ship", StepName = "publish", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "ship", step);
        ArrangeEnvironment(new EnvironmentCheck
        {
            Name = "callback",
            Type = EnvironmentCheckType.RestCallback,
            IsRequired = true,
            Configuration = configuration
        });

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Failed, step.Status);
        _repoMock.DidNotReceive().TrackTask(Arg.Any<ServerTask>());
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/health")]
    [InlineData("http://localhost/health")]
    [InlineData("http://10.1.2.3/health")]
    [InlineData("http://172.16.0.1/health")]
    [InlineData("http://192.168.1.1/health")]
    [InlineData("http://169.254.169.254/health")]
    [InlineData("http://100.64.0.1/health")]
    public async Task AdvanceStageAsync_UnsafeRestEnvironmentCheck_FailsClosedBeforeHttp(string url)
    {
        var yaml = EnvironmentStageYaml();
        var step = new PipelineStepRun { Id = 2, StageName = "ship", StepName = "publish", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "ship", step);
        ArrangeEnvironment(new EnvironmentCheck
        {
            Name = "callback",
            Type = EnvironmentCheckType.RestCallback,
            IsRequired = true,
            Configuration = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["url"] = url })
        });

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Failed, step.Status);
        _repoMock.DidNotReceive().TrackTask(Arg.Any<ServerTask>());
        _httpClientFactoryMock.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task AdvanceStageAsync_PublicRestEnvironmentCheck_UsesNamedClientAndDispatchesOnSuccess()
    {
        var yaml = EnvironmentStageYaml();
        var step = new PipelineStepRun { Id = 2, StageName = "ship", StepName = "publish", PipelineRunId = 1 };
        ArrangeAdvanceToStage(yaml, "ship", step);
        ArrangeEnvironment(new EnvironmentCheck
        {
            Name = "callback",
            Type = EnvironmentCheckType.RestCallback,
            IsRequired = true,
            TimeoutSeconds = 5,
            Configuration = "{\"url\":\"https://1.1.1.1/health\"}"
        });
        _httpClientFactoryMock.CreateClient(PipelinesModuleExtensions.EnvironmentCheckHttpClient)
            .Returns(new HttpClient(new StaticResponseHandler(System.Net.HttpStatusCode.NoContent)));

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Assigned, step.Status);
        _repoMock.Received(1).TrackTask(Arg.Any<ServerTask>());
        _httpClientFactoryMock.Received(1).CreateClient(PipelinesModuleExtensions.EnvironmentCheckHttpClient);
    }

    [Fact]
    public async Task AdvanceStageAsync_OsMatrix_DispatchesLegsToOsAppropriateAgents()
    {
        // S-FEAT-21: an `os` matrix axis fans the same steps out across OS-appropriate runners.
        var yaml = """
            name: matrix
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: fan
                matrix:
                  os: [linux, windows]
                steps:
                  - name: pkg
                    shell: echo pkg
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns(
            [
                new PipelineStepRun { Id = 2, StageName = "fan", StepName = "pkg [linux]", MatrixLeg = "linux", PipelineRunId = 1 },
                new PipelineStepRun { Id = 3, StageName = "fan", StepName = "pkg [windows]", MatrixLeg = "windows", PipelineRunId = 1 }
            ]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.GetActiveStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([]);
        // Stage resolves to a Linux runner (no os constraint); the windows leg resolves its own.
        _repoMock.FindAnyOnlineRunnerAsync(Arg.Any<int?>(), OsType.Unknown, Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-runner", OsType = OsType.Linux });
        _repoMock.FindAnyOnlineRunnerAsync(Arg.Any<int?>(), OsType.Windows, Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 20, Name = "windows-runner", OsType = OsType.Windows });
        _repoMock.TrackTask(Arg.Any<ServerTask>());
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        // Linux leg lands on the Linux runner; Windows leg lands on the Windows runner.
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t => t.Name == "pkg [linux]" && t.ServerId == 10));
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t => t.Name == "pkg [windows]" && t.ServerId == 20));
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_OsMatrix_NoRunnerForLeg_FailsRunInsteadOfStalling()
    {
        // S-FEAT-21 regression: a matrix os leg with no available runner must fail the run, not
        // strand it Running with no in-flight task (which would never re-trigger advancement).
        var yaml = """
            name: matrix
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: fan
                matrix:
                  os: [windows]
                steps:
                  - name: pkg
                    shell: echo pkg
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Running,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 2, StageName = "fan", StepName = "pkg [windows]", MatrixLeg = "windows", PipelineRunId = 1 }]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.GetActiveStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([]);
        // Stage resolves to a Linux runner; there is NO Windows runner for the windows leg.
        _repoMock.FindAnyOnlineRunnerAsync(Arg.Any<int?>(), OsType.Unknown, Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-runner", OsType = OsType.Linux });
        _repoMock.FindAnyOnlineRunnerAsync(Arg.Any<int?>(), OsType.Windows, Arg.Any<CancellationToken>())
            .Returns((Server?)null);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.AppendRunWarningsAsync(1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        _repoMock.DidNotReceive().TrackTask(Arg.Any<ServerTask>());
        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
        await _repoMock.Received().AppendRunWarningsAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(w => w.Any(m => m.Contains("no online runner"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_TriggerStep_MissingChildIsMaterializedFromImmutableGitRevision()
    {
        const string commit = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var parentYaml = """
            name: release
            trigger: manual
            stages:
              - name: qa
                agent: linux-01
                steps:
                  - name: compatibility
                    type: trigger
                    pipeline: qa-with-rollback
            """;
        var childYaml = """
            name: qa-with-rollback
            trigger: manual
            stages:
              - name: validate
                agent: linux-01
                steps:
                  - name: test
                    shell: dotnet test
            """;
        var step = new PipelineStepRun { Id = 2, StageName = "qa", StepName = "compatibility", PipelineRunId = 1 };
        ArrangeAdvanceToStage(parentYaml, "qa", step);
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns(_ => step.Status == TaskExecutionStatus.Pending ? [step] : []);
        var parent = new Pipeline
        {
            Id = 1,
            Name = "release",
            ProjectId = 7,
            CreatedByUsername = "alice",
            YamlDefinition = parentYaml
        };
        var parentRun = new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            Status = PipelineStatus.Running,
            CommitHash = commit,
            BranchName = "main",
            AdditionalVariablesJson = "{}",
            ResolvedVariablesJson = "{}",
            Pipeline = parent
        };
        Pipeline? materialized = null;
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(parentRun);
        _repoMock.GetPipelineProjectIdAsync(parent, Arg.Any<CancellationToken>()).Returns(7);
        _repoMock.FindPipelineByNameAndProjectAsync("qa-with-rollback", 7, Arg.Any<CancellationToken>())
            .Returns(_ => materialized);
        _pipelineGitMock.ReadProjectPipelineYamlAtRevisionAsync(7, "qa-with-rollback", commit, Arg.Any<CancellationToken>())
            .Returns(childYaml);
        _repoMock.AddPipelineAsync(Arg.Do<Pipeline>(pipeline =>
            {
                pipeline.Id = 2;
                materialized = pipeline;
            }), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.FindPipelineAsync(2, Arg.Any<CancellationToken>()).Returns(_ => materialized);
        _repoMock.GetPipelineProjectIdAsync(Arg.Is<Pipeline>(pipeline => pipeline.Id == 2), Arg.Any<CancellationToken>())
            .Returns(7);
        _repoMock.FindCandidateTargetServerIdsAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<OsType>(),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([42]);
        _authzMock.HasPermissionAsync("alice", ResourceType.Server, 42, Permission.Admin, Arg.Any<CancellationToken>())
            .Returns(false);
        _repoMock.AppendRunWarningsAsync(1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(materialized);
        Assert.Equal("qa-with-rollback", materialized.Name);
        Assert.Equal(7, materialized.ProjectId);
        Assert.Equal("main", materialized.SourceBranch);
        Assert.Equal("alice", materialized.CreatedByUsername);
        Assert.Equal(childYaml, materialized.YamlDefinition);
        await _repoMock.Received(1).AddPipelineAsync(materialized, Arg.Any<CancellationToken>());
        await _pipelineGitMock.Received(2).ReadProjectPipelineYamlAtRevisionAsync(
            7, "qa-with-rollback", commit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_TriggerStep_ChildOwnerLacksAdmin_RunsCleanupBeforeFinalizing()
    {
        // Prod regression (run #5): a `type: trigger` step whose child pipeline's OWNER fails the F-EXEC-1b
        // Server.Admin gate settles Failed synchronously with NO child run. The scheduler must persist that
        // failure and dispatch System:Cleanup rather than waiting for the 1h backstop or finalizing too early;
        // the warning must also NAME the offending owner so the cause is self-evident.
        var parentYaml = """
            name: aetheus-release
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: echo build
              - name: ci
                agent: linux-01
                depends_on:
                  - build
                steps:
                  - name: Build & test
                    type: trigger
                    pipeline: aetheus-ci
            """;
        var childYaml = """
            name: aetheus-ci
            trigger: manual
            stages:
              - name: compile
                os: linux
                steps:
                  - name: build
                    shell: dotnet build
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>()).Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>()).Returns([]);
        var parentRun = new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            Status = PipelineStatus.Running,
            ResolvedVariablesJson = "{}",
            Pipeline = new Pipeline { Id = 1, Name = "aetheus-release", YamlDefinition = parentYaml }
        };
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(parentRun);
        var triggerStep = new PipelineStepRun { Id = 2, StageName = "ci", StepName = "Build & test", PipelineRunId = 1 };
        var cleanupStep = new PipelineStepRun
        {
            Id = 3,
            StageName = PipelineRunService.SystemCleanupStage,
            StepName = "Cleanup",
            PipelineRunId = 1,
            IsSystem = true
        };
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([triggerStep], [cleanupStep]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.GetTerminalStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build", "ci"]);
        _repoMock.HasAnyFailedStepInRunAsync(1, Arg.Any<CancellationToken>()).Returns(false, true);
        _repoMock.GetActiveStageNamesAsync(1, Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetPipelineIdForRunAsync(1, Arg.Any<CancellationToken>()).Returns(1);
        _repoMock.GetPipelineOrganizationIdAsync(1, Arg.Any<CancellationToken>()).Returns((int?)null);
        // The ci stage still needs a runner resolved (the trigger step itself uses none).
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01" });

        // Trigger resolution: the child pipeline exists, is owned by the un-authorizable "system" sentinel,
        // and resolves to runner id 1 - on which "system" has no Server.Admin (authz mock defaults false).
        var childPipeline = new Pipeline { Id = 2, Name = "aetheus-ci", CreatedByUsername = "system", YamlDefinition = childYaml };
        _repoMock.GetPipelineProjectIdAsync(Arg.Any<Pipeline>(), Arg.Any<CancellationToken>()).Returns(7);
        _repoMock.FindPipelineByNameAndProjectAsync("aetheus-ci", 7, Arg.Any<CancellationToken>()).Returns(childPipeline);
        _repoMock.FindPipelineAsync(2, Arg.Any<CancellationToken>()).Returns(childPipeline);
        _repoMock.FindCandidateTargetServerIdsAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<OsType>(),
                Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([1]);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.AppendRunWarningsAsync(1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        // No child run is created. The synchronous failure is persisted, then cleanup becomes the only
        // in-flight task; its completion will re-enter the scheduler and finalize the original failure.
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task => task.Name == "Cleanup"));
        Assert.Equal(TaskExecutionStatus.Assigned, cleanupStep.Status);
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
        // The warning names the offending owner ('system') so the cause is self-evident.
        await _repoMock.Received().AppendRunWarningsAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(w => w.Any(m => m.Contains("'system'") && m.Contains("lacks Server.Admin"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_ContainerIsolation_DispatchesHardenedContainerTask()
    {
        var yaml = """
            name: deploy
            trigger: manual
            isolation:
              mode: container
              image: mcr.microsoft.com/dotnet/sdk:9.0
              network: none
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: deploy
                agent: linux-01
                steps:
                  - name: deploy-app
                    shell: deploy.sh
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 2, StageName = "deploy", StepName = "deploy-app", PipelineRunId = 1 }]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        // Container isolation requires a Docker-capable runner (fail-closed enforcement).
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01", DockerAvailable = true });
        _repoMock.TrackTask(Arg.Any<ServerTask>());
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t =>
            t.Name == "deploy-app"
            && t.Executor == ExecutorType.Container
            && t.ContainerImage == "mcr.microsoft.com/dotnet/sdk:9.0"
            && t.ContainerNetwork == "none"));
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(Arg.Any<int>(), PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_ContainerIsolation_NoDocker_BlocksRun()
    {
        var yaml = """
            name: deploy
            trigger: manual
            isolation:
              mode: container
              image: mcr.microsoft.com/dotnet/sdk:9.0
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
              - name: deploy
                agent: linux-01
                steps:
                  - name: deploy-app
                    shell: deploy.sh
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Running,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 2, StageName = "deploy", StepName = "deploy-app", PipelineRunId = 1 }]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        // Runner has NO Docker - container isolation must block, not silently downgrade.
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01", DockerAvailable = false });
        _repoMock.HasAnyFailedStepInRunAsync(1, Arg.Any<CancellationToken>())
            .Returns(false);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.AppendRunWarningsAsync(1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        _repoMock.DidNotReceive().TrackTask(Arg.Is<ServerTask>(t => t.Name == "deploy-app"));
        await _repoMock.Received(1).AppendRunWarningsAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(w => w.Any(m => m.Contains("no Docker available"))),
            Arg.Any<CancellationToken>());
        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    // --- ResolveVariablesAsync tests (via TriggerRunAsync) ---

    [Fact]
    public async Task TriggerRunAsync_InlineVarsOverriddenByLibrary_LibraryWins()
    {
        var yaml = """
            name: deploy
            trigger: manual
            variables:
              APP_PORT: "3000"
              DB_HOST: inline-db
            variable_libraries:
              - my-lib
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: run
                    shell: echo $(APP_PORT) $(DB_HOST)
            """;

        SetupTriggerRunMocks(yaml);

        _varLibMock.ResolveLibrariesWithNamesAsync(Arg.Any<List<string>>(), null, Arg.Any<CancellationToken>())
            .Returns((new Dictionary<string, string> { ["APP_PORT"] = "8080" }, new HashSet<string> { "my-lib" }));

        await _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken);

        _repoMock.Received().TrackTask(Arg.Is<ServerTask>(t => t.Command.Contains("8080") && t.Command.Contains("inline-db")));
    }

    [Fact]
    public async Task TriggerRunAsync_VaultOverridesLibraryAndInline()
    {
        var yaml = """
            name: deploy
            trigger: manual
            variables:
              SECRET_KEY: inline-secret
            variable_libraries:
              - my-lib
            vaults:
              - my-vault
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: run
                    shell: echo $(SECRET_KEY)
            """;

        SetupTriggerRunMocks(yaml);

        _varLibMock.ResolveLibrariesWithNamesAsync(Arg.Any<List<string>>(), null, Arg.Any<CancellationToken>())
            .Returns((new Dictionary<string, string> { ["SECRET_KEY"] = "lib-secret" }, new HashSet<string> { "my-lib" }));
        _vaultMock.ResolveVaultSecretsWithNamesAsync(Arg.Any<List<string>>(), null, Arg.Any<CancellationToken>())
            .Returns((new Dictionary<string, string> { ["SECRET_KEY"] = "vault-secret" }, new HashSet<string> { "my-vault" }));

        await _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken);

        _repoMock.Received().TrackTask(Arg.Is<ServerTask>(t => t.Command.Contains("vault-secret")));
    }

    [Fact]
    public async Task TriggerRunAsync_AdditionalVarsOverrideAll()
    {
        var yaml = """
            name: deploy
            trigger: manual
            variables:
              ENV: dev
            vaults:
              - my-vault
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: run
                    shell: echo $(ENV)
            """;

        SetupTriggerRunMocks(yaml);

        _vaultMock.ResolveVaultSecretsWithNamesAsync(Arg.Any<List<string>>(), null, Arg.Any<CancellationToken>())
            .Returns((new Dictionary<string, string> { ["ENV"] = "vault-env" }, new HashSet<string> { "my-vault" }));

        var additionalVars = new Dictionary<string, string> { ["ENV"] = "production" };
        await _sut.TriggerRunAsync(1, additionalVars, ct: TestContext.Current.CancellationToken);

        _repoMock.Received().TrackTask(Arg.Is<ServerTask>(t => t.Command.Contains("production")));
    }

    [Fact]
    public async Task TriggerRunAsync_MissingVariable_LeftAsIs()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: run
                    shell: echo $(UNDEFINED_VAR) done
            """;

        SetupTriggerRunMocks(yaml);

        await _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken);

        _repoMock.Received(1).TrackTask(
            Arg.Is<ServerTask>(t => t.Command.Contains("$(UNDEFINED_VAR)")));
    }

    [Fact]
    public async Task TriggerRunAsync_MultipleVarsInSameCommand_AllSubstituted()
    {
        var yaml = """
            name: deploy
            trigger: manual
            variables:
              HOST: localhost
              PORT: "5000"
              PATH_PREFIX: /api
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: run
                    shell: curl $(HOST):$(PORT)$(PATH_PREFIX)/health
            """;

        SetupTriggerRunMocks(yaml);

        await _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken);

        _repoMock.Received().TrackTask(Arg.Is<ServerTask>(t => t.Command.Contains("curl localhost:5000/api/health")));
    }

    [Fact]
    public async Task TriggerRunAsync_EmptyVarDict_NoSubstitution()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: run
                    shell: echo hello
            """;

        SetupTriggerRunMocks(yaml);

        await _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken);

        _repoMock.Received().TrackTask(Arg.Is<ServerTask>(t => t.Command.Contains("echo hello")));
    }

    [Fact]
    public async Task TriggerRunAsync_CaseInsensitiveKeyMatching()
    {
        var yaml = """
            name: deploy
            trigger: manual
            variables:
              my_var: value1
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: run
                    shell: echo $(MY_VAR)
            """;

        SetupTriggerRunMocks(yaml);

        await _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken);

        _repoMock.Received().TrackTask(Arg.Is<ServerTask>(t => t.Command.Contains("echo value1")));
    }

    // --- DryRunAsync ---

    [Fact]
    public async Task DryRunAsync_PipelineNotFound_ReturnsNull()
    {
        _repoMock.FindPipelineAsync(99, Arg.Any<CancellationToken>())
            .Returns((Pipeline?)null);

        var result = await _sut.DryRunAsync(99, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DryRunAsync_InvalidYaml_ReturnsNull()
    {
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Bad", YamlDefinition = "{{invalid", Runs = [] });

        var result = await _sut.DryRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DryRunAsync_MissingLibrary_ReturnsWarning()
    {
        var yaml = """
            name: deploy
            trigger: manual
            variable_libraries:
              - non-existent-lib
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: run
                    shell: echo hi
            """;

        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = yaml, Runs = [] });
        _varLibMock.ResolveLibrariesWithNamesAsync(Arg.Any<List<string>>(), null, Arg.Any<CancellationToken>())
            .Returns((new Dictionary<string, string>(), new HashSet<string>()));

        var result = await _sut.DryRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Contains(result.Warnings, w => w.Contains("non-existent-lib") && w.Contains("not found"));
    }

    [Fact]
    public async Task DryRunAsync_MissingVault_ReturnsWarning()
    {
        var yaml = """
            name: deploy
            trigger: manual
            vaults:
              - missing-vault
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: run
                    shell: echo hi
            """;

        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = yaml, Runs = [] });
        _vaultMock.ResolveVaultSecretsWithNamesAsync(Arg.Any<List<string>>(), null, Arg.Any<CancellationToken>())
            .Returns((new Dictionary<string, string>(), new HashSet<string>()));

        var result = await _sut.DryRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Contains(result.Warnings, w => w.Contains("missing-vault") && w.Contains("not found"));
    }

    [Fact]
    public async Task DryRunAsync_ValidPipeline_ReturnsResolvedStages()
    {
        var yaml = """
            name: deploy
            trigger: manual
            variables:
              HOST: localhost
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: echo $(HOST)
            """;

        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = yaml, Runs = [] });

        var result = await _sut.DryRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Stages);
        Assert.Equal("build", result.Stages[0].StageName);
        Assert.Equal("echo localhost", result.Stages[0].Steps[0].ResolvedCommand);
        Assert.Empty(result.Warnings);
    }

    // --- PreflightAsync ---

    [Fact]
    public async Task PreflightAsync_PipelineNotFound_ReturnsNull()
    {
        _repoMock.FindPipelineAsync(99, Arg.Any<CancellationToken>())
            .Returns((Pipeline?)null);

        Assert.Null(await _sut.PreflightAsync(99, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PreflightAsync_FlagsStagesWithNoMatchingServer()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: make
              - name: ship
                agent: ghost-agent
                steps:
                  - name: deploy
                    shell: deploy.sh
            """;
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = yaml, Runs = [] });
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 7, Name = "srv-linux" });
        _repoMock.FindOnlineServerByAgentAsync("ghost-agent", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns((Server?)null);

        var result = await _sut.PreflightAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result.Stages.Count);

        var build = result.Stages.Single(s => s.StageName == "build");
        Assert.True(build.Resolved);
        Assert.Equal("srv-linux", build.ServerName);
        Assert.Equal(PreflightTargetKind.Agent, build.TargetKind);
        Assert.Equal("linux-01", build.Target);
        Assert.Null(build.Reason);

        var ship = result.Stages.Single(s => s.StageName == "ship");
        Assert.False(ship.Resolved);
        Assert.Null(ship.ServerName);
        Assert.NotNull(ship.Reason);
        Assert.Contains("ghost-agent", ship.Reason);
    }

    [Fact]
    public async Task PreflightAsync_DeployStep_UsesDeploymentTargetResolver()
    {
        var yaml = """
            name: deploy
            stages:
              - name: ship
                agent: deploy-01
                steps:
                  - name: publish
                    type: deploy
                    app: api
            """;
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = yaml, Runs = [] });
        _repoMock.FindOnlineDeployTargetAsync(
                null, null, "deploy-01", OsType.Unknown, null, Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 8, Name = "deployment-host" });

        var result = await _sut.PreflightAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal("deployment-host", Assert.Single(result!.Stages).ServerName);
        await _repoMock.Received(1).FindOnlineDeployTargetAsync(
            null, null, "deploy-01", OsType.Unknown, null, Arg.Any<CancellationToken>());
        await _repoMock.DidNotReceive().FindOnlineServerByAgentAsync(
            Arg.Any<string>(), Arg.Any<OsType>(), Arg.Any<CancellationToken>());
    }

    // --- Shared setup helper ---

    private void SetupTriggerRunMocks(string yaml)
    {
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = yaml, Runs = [] });
        _repoMock.AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.TrackPipelineStepRun(Arg.Any<PipelineStepRun>());
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.GetPendingStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 1, StageName = "build", StepName = "run", PipelineRunId = 1 }]);
        _repoMock.GetCompletedStageNamesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage]);
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01" });
        _repoMock.FindAnyOnlineRunnerAsync(Arg.Any<int?>(), Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01" });
        _repoMock.GetPipelineRunWithPipelineAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { Name = "Deploy", YamlDefinition = yaml }
            });
        _repoMock.TrackTask(Arg.Any<ServerTask>());
        _repoMock.GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Running,
                Pipeline = new Pipeline { Name = "Deploy" },
                StepRuns = []
            });
    }

    // --- P-24: Matrix Expansion ---

    [Fact]
    public async Task TriggerRunAsync_MatrixStage_CreatesMultipleStepRuns()
    {
        var yaml = """
            name: matrix-test
            trigger: manual
            stages:
              - name: test
                agent: linux-01
                matrix:
                  os:
                    - ubuntu
                    - windows
                  arch:
                    - x64
                    - arm64
                steps:
                  - name: run-test
                    shell: echo testing
            """;

        var stepRunsAdded = new List<PipelineStepRun>();
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Matrix", YamlDefinition = yaml, Runs = [] });
        _repoMock.AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.When(x => x.TrackPipelineStepRun(Arg.Any<PipelineStepRun>())).Do(ci => stepRunsAdded.Add(ci.Arg<PipelineStepRun>()));
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.GetPendingStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Running,
                Pipeline = new Pipeline { Name = "Matrix" },
                StepRuns = []
            });

        await _sut.TriggerRunAsync(1, ct: TestContext.Current.CancellationToken);

        // 2 os × 2 arch = 4 user step runs + 2 system (Prepare + Cleanup) = 6
        Assert.Equal(6, stepRunsAdded.Count);
        Assert.Contains(stepRunsAdded, s => s.MatrixLeg == "ubuntu-x64");
        Assert.Contains(stepRunsAdded, s => s.MatrixLeg == "ubuntu-arm64");
        Assert.Contains(stepRunsAdded, s => s.MatrixLeg == "windows-x64");
        Assert.Contains(stepRunsAdded, s => s.MatrixLeg == "windows-arm64");
    }

    // --- P-27: Retry Decrement ---

    [Fact]
    public async Task AdvanceStageAsync_FailedStepWithRetry_DecrementsAndResets()
    {
        var yaml = """
            name: retry-test
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: flaky
                    shell: make
                    retry_count: 2
            """;

        var failedStep = new PipelineStepRun
        {
            Id = 1,
            StageName = "build",
            StepName = "flaky",
            Status = TaskExecutionStatus.Failed,
            RetryCount = 2,
            ContinueOnError = false,
            ExitCode = 1,
            StartedAt = DateTime.UtcNow.AddSeconds(-5),
            CompletedAt = DateTime.UtcNow
        };

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([failedStep]);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, failedStep.RetryCount); // Decremented from 2 to 1
        Assert.Equal(TaskExecutionStatus.Pending, failedStep.Status);
        Assert.Null(failedStep.ExitCode);
        Assert.Null(failedStep.StartedAt);
        Assert.Null(failedStep.CompletedAt);
        // Should NOT advance to next stage or mark as failed
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(Arg.Any<int>(), Arg.Any<PipelineStatus>(), Arg.Any<CancellationToken>());
    }

    // --- P-27: Continue-on-Error ---

    [Fact]
    public async Task AdvanceStageAsync_AllFailuresContinuable_AdvancesToNextStage()
    {
        var yaml = """
            name: continue-test
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: optional
                    shell: echo maybe
                    continue_on_error: true
              - name: deploy
                agent: linux-01
                steps:
                  - name: deploy-app
                    shell: deploy.sh
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun
            {
                Id = 1, StageName = "build", StepName = "optional",
                Status = TaskExecutionStatus.Failed, RetryCount = 0, ContinueOnError = true
            }]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 2, StageName = "deploy", StepName = "deploy-app", PipelineRunId = 1 }]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01" });
        _repoMock.TrackTask(Arg.Any<ServerTask>());
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(t => t.Name == "deploy-app"));
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(Arg.Any<int>(), PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_MixedFailures_NotAllContinuable_FailsRun()
    {
        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([
                new PipelineStepRun { Id = 1, StageName = "build", StepName = "optional", Status = TaskExecutionStatus.Failed, RetryCount = 0, ContinueOnError = true },
                new PipelineStepRun { Id = 2, StageName = "build", StepName = "critical", Status = TaskExecutionStatus.Failed, RetryCount = 0, ContinueOnError = false }
            ]);
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    // --- CancelRunAsync ---

    [Fact]
    public async Task CancelRunAsync_RunNotFound_ReturnsFalse()
    {
        _repoMock.GetPipelineRunWithPipelineAsync(99, Arg.Any<CancellationToken>())
            .Returns((PipelineRun?)null);

        var result = await _sut.CancelRunAsync(99, ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task CancelRunAsync_AlreadyCompleted_ReturnsFalse()
    {
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun { Id = 1, Status = PipelineStatus.Success });

        var result = await _sut.CancelRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task CancelRunAsync_RunningPipeline_CancelsAndReturnsTrue()
    {
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun { Id = 1, Status = PipelineStatus.Running });
        _repoMock.CancelPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.TryTransitionPipelineRunStatusAsync(
                1, PipelineStatus.Running, PipelineStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.CancelRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(result);
        await _repoMock.Received(1).CancelPendingStepRunsAsync(1, Arg.Any<CancellationToken>());
        await _repoMock.Received(1).TryTransitionPipelineRunStatusAsync(
            1, PipelineStatus.Running, PipelineStatus.Cancelled, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelRunAsync_ParentPipeline_CancelsEveryTriggeredDescendant()
    {
        var runs = new Dictionary<int, PipelineRun>
        {
            [1] = new() { Id = 1, PipelineId = 11, Pipeline = new Pipeline { Id = 11 }, Status = PipelineStatus.Running },
            [2] = new() { Id = 2, PipelineId = 12, Pipeline = new Pipeline { Id = 12 }, Status = PipelineStatus.WaitingForApproval },
            [3] = new() { Id = 3, PipelineId = 13, Pipeline = new Pipeline { Id = 13 }, Status = PipelineStatus.Pending }
        };
        _repoMock.GetPipelineRunWithPipelineAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => runs.GetValueOrDefault(call.ArgAt<int>(0)));
        _repoMock.GetTriggeredChildRunIdsAsync(1, Arg.Any<CancellationToken>()).Returns([2]);
        _repoMock.GetTriggeredChildRunIdsAsync(2, Arg.Any<CancellationToken>()).Returns([3]);
        _repoMock.GetTriggeredChildRunIdsAsync(3, Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.TryTransitionPipelineRunStatusAsync(
                Arg.Any<int>(), Arg.Any<PipelineStatus>(), PipelineStatus.Cancelled, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.CancelRunAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.True(result);
        await _repoMock.Received(1).TryTransitionPipelineRunStatusAsync(
            1, PipelineStatus.Running, PipelineStatus.Cancelled, Arg.Any<CancellationToken>());
        await _repoMock.Received(1).TryTransitionPipelineRunStatusAsync(
            2, PipelineStatus.WaitingForApproval, PipelineStatus.Cancelled, Arg.Any<CancellationToken>());
        await _repoMock.Received(1).TryTransitionPipelineRunStatusAsync(
            3, PipelineStatus.Pending, PipelineStatus.Cancelled, Arg.Any<CancellationToken>());
        await _repoMock.Received(3).CancelPendingStepRunsAsync(
            Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _clientProxyMock.Received(3).SendCoreAsync(
            "PipelineRunCancelled", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    // --- ResumeAfterApprovalAsync ---

    [Fact]
    public async Task ResumeAfterApprovalAsync_NotWaiting_DoesNothing()
    {
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun { Id = 1, Status = PipelineStatus.Running });

        await _sut.ResumeAfterApprovalAsync(1, ct: TestContext.Current.CancellationToken);

        await _repoMock.DidNotReceive().TryTransitionPipelineRunStatusAsync(
            Arg.Any<int>(), Arg.Any<PipelineStatus>(), Arg.Any<PipelineStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAfterApprovalAsync_RunNotFound_DoesNothing()
    {
        _repoMock.GetPipelineRunWithPipelineAsync(99, Arg.Any<CancellationToken>())
            .Returns((PipelineRun?)null);

        await _sut.ResumeAfterApprovalAsync(99, ct: TestContext.Current.CancellationToken);

        await _repoMock.DidNotReceive().TryTransitionPipelineRunStatusAsync(
            Arg.Any<int>(), Arg.Any<PipelineStatus>(), Arg.Any<PipelineStatus>(), Arg.Any<CancellationToken>());
    }

    // --- FindReadyStages ---

    [Fact]
    public void FindReadyStages_NoDependencies_ReturnsAllPendingStages()
    {
        var definition = new PipelineYamlDefinition
        {
            Stages = [
                new PipelineStageDefinition { Name = "build" },
                new PipelineStageDefinition { Name = "test" }
            ]
        };
        var pendingSteps = new List<PipelineStepRun>
        {
            new() { StageName = "build" },
            new() { StageName = "test" }
        };

        var result = InvokeFindReadyStages(pendingSteps, definition, [PipelineRunService.SystemPrepareStage]);

        Assert.Equal(2, result.Count);
        Assert.Contains("build", result);
        Assert.Contains("test", result);
    }

    [Fact]
    public void FindReadyStages_WithUnmetDependency_ExcludesStage()
    {
        var definition = new PipelineYamlDefinition
        {
            Stages = [
                new PipelineStageDefinition { Name = "build" },
                new PipelineStageDefinition { Name = "deploy", DependsOn = ["build"] }
            ]
        };
        var pendingSteps = new List<PipelineStepRun>
        {
            new() { StageName = "build" },
            new() { StageName = "deploy" }
        };

        var result = InvokeFindReadyStages(pendingSteps, definition, [PipelineRunService.SystemPrepareStage]);

        Assert.Single(result);
        Assert.Equal("build", result[0]);
    }

    [Fact]
    public void FindReadyStages_WithMetDependency_IncludesStage()
    {
        var definition = new PipelineYamlDefinition
        {
            Stages = [
                new PipelineStageDefinition { Name = "build" },
                new PipelineStageDefinition { Name = "deploy", DependsOn = ["build"] }
            ]
        };
        var pendingSteps = new List<PipelineStepRun> { new() { StageName = "deploy" } };

        var result = InvokeFindReadyStages(pendingSteps, definition, [PipelineRunService.SystemPrepareStage, "build"]);

        Assert.Single(result);
        Assert.Equal("deploy", result[0]);
    }

    [Fact]
    public void FindReadyStages_UnknownStage_ExcludesIt()
    {
        var definition = new PipelineYamlDefinition
        {
            Stages = [new PipelineStageDefinition { Name = "build" }]
        };
        var pendingSteps = new List<PipelineStepRun> { new() { StageName = "unknown" } };

        var result = InvokeFindReadyStages(pendingSteps, definition, []);

        Assert.Empty(result);
    }

    [Fact]
    public void FindReadyStages_MultipleDepsMet_IncludesStage()
    {
        var definition = new PipelineYamlDefinition
        {
            Stages = [
                new PipelineStageDefinition { Name = "build" },
                new PipelineStageDefinition { Name = "test" },
                new PipelineStageDefinition { Name = "deploy", DependsOn = ["build", "test"] }
            ]
        };
        var pendingSteps = new List<PipelineStepRun> { new() { StageName = "deploy" } };

        var result = InvokeFindReadyStages(pendingSteps, definition, [PipelineRunService.SystemPrepareStage, "build", "test"]);

        Assert.Single(result);
        Assert.Equal("deploy", result[0]);
    }

    [Fact]
    public void FindReadyStages_PartialDepsMet_ExcludesStage()
    {
        var definition = new PipelineYamlDefinition
        {
            Stages = [
                new PipelineStageDefinition { Name = "deploy", DependsOn = ["build", "test"] }
            ]
        };
        var pendingSteps = new List<PipelineStepRun> { new() { StageName = "deploy" } };

        var result = InvokeFindReadyStages(pendingSteps, definition, [PipelineRunService.SystemPrepareStage, "build"]);

        Assert.Empty(result);
    }

    [Fact]
    public void FindReadyStages_AlwaysStage_AcceptsTerminalFailedDependency()
    {
        var definition = new PipelineYamlDefinition
        {
            Stages =
            [
                new PipelineStageDefinition { Name = "e2e" },
                new PipelineStageDefinition { Name = "teardown", Condition = "always()", DependsOn = ["e2e"] }
            ]
        };
        var pendingSteps = new List<PipelineStepRun> { new() { StageName = "teardown" } };

        var result = InvokeFindReadyStages(
            pendingSteps,
            definition,
            [PipelineRunService.SystemPrepareStage],
            [PipelineRunService.SystemPrepareStage, "e2e"]);

        Assert.Equal(["teardown"], result);
    }

    [Fact]
    public void FindReadyStages_NormalStage_RejectsTerminalFailedDependency()
    {
        var definition = new PipelineYamlDefinition
        {
            Stages =
            [
                new PipelineStageDefinition { Name = "e2e" },
                new PipelineStageDefinition { Name = "deploy", DependsOn = ["e2e"] }
            ]
        };
        var pendingSteps = new List<PipelineStepRun> { new() { StageName = "deploy" } };

        var result = InvokeFindReadyStages(
            pendingSteps,
            definition,
            [PipelineRunService.SystemPrepareStage],
            [PipelineRunService.SystemPrepareStage, "e2e"]);

        Assert.Empty(result);
    }

    [Fact]
    public void FindReadyStages_SystemCleanup_AcceptsTerminalFailedUserStages()
    {
        var definition = new PipelineYamlDefinition
        {
            Stages =
            [
                new PipelineStageDefinition { Name = "e2e" },
                new PipelineStageDefinition { Name = "teardown", Condition = "always()", DependsOn = ["e2e"] }
            ]
        };
        var pendingSteps = new List<PipelineStepRun>
        {
            new() { StageName = PipelineRunService.SystemCleanupStage, IsSystem = true }
        };

        var result = InvokeFindReadyStages(
            pendingSteps,
            definition,
            [PipelineRunService.SystemPrepareStage, "teardown"],
            [PipelineRunService.SystemPrepareStage, "e2e", "teardown"]);

        Assert.Equal([PipelineRunService.SystemCleanupStage], result);
    }

    [Fact]
    public void FindReadyStages_UserStageStillRunning_DoesNotUnlockSystemCleanup()
    {
        var definition = new PipelineYamlDefinition
        {
            Stages =
            [
                new PipelineStageDefinition { Name = "deploy" },
                new PipelineStageDefinition { Name = "teardown" }
            ]
        };
        var pendingSteps = new List<PipelineStepRun>
        {
            new() { StageName = PipelineRunService.SystemCleanupStage, IsSystem = true }
        };

        var result = InvokeFindReadyStages(
            pendingSteps,
            definition,
            [PipelineRunService.SystemPrepareStage],
            [PipelineRunService.SystemPrepareStage, "deploy"]);

        Assert.Empty(result);
    }

    // --- MarkStepsAs ---

    [Fact]
    public void MarkStepsAs_SetsStatusAndTimestamp()
    {
        var steps = new List<PipelineStepRun>
        {
            new() { Status = TaskExecutionStatus.Pending },
            new() { Status = TaskExecutionStatus.Pending }
        };

        InvokeMarkStepsAs(steps, TaskExecutionStatus.Cancelled);

        Assert.All(steps, s => Assert.Equal(TaskExecutionStatus.Cancelled, s.Status));
        Assert.All(steps, s => Assert.NotNull(s.CompletedAt));
    }

    [Fact]
    public void MarkStepsAs_EmptyList_NoOp()
    {
        InvokeMarkStepsAs([], TaskExecutionStatus.Failed);
    }

    // --- ResolveLegVariables ---

    [Fact]
    public void ResolveLegVariables_EmptyLegKey_ReturnsCopyOfStageVars()
    {
        var stageVars = new Dictionary<string, string> { ["HOST"] = "localhost" };

        var result = InvokeResolveLegVariables(stageVars, "", []);

        Assert.Equal("localhost", result["HOST"]);
        stageVars["NEW"] = "val";
        Assert.False(result.ContainsKey("NEW"));
    }

    [Fact]
    public void ResolveLegVariables_MatchingLeg_MergesLegVars()
    {
        var stageVars = new Dictionary<string, string> { ["HOST"] = "localhost" };
        var matrixLegs = new List<Dictionary<string, string>>
        {
            new() { ["os"] = "ubuntu", ["arch"] = "x64" }
        };

        var result = InvokeResolveLegVariables(stageVars, "ubuntu-x64", matrixLegs);

        Assert.Equal("localhost", result["HOST"]);
        Assert.Equal("ubuntu", result["os"]);
        Assert.Equal("x64", result["arch"]);
    }

    [Fact]
    public void ResolveLegVariables_NoMatchingLeg_ReturnsBaseVarsOnly()
    {
        var stageVars = new Dictionary<string, string> { ["HOST"] = "localhost" };
        var matrixLegs = new List<Dictionary<string, string>>
        {
            new() { ["os"] = "ubuntu" }
        };

        var result = InvokeResolveLegVariables(stageVars, "windows", matrixLegs);

        Assert.Equal("localhost", result["HOST"]);
        Assert.False(result.ContainsKey("os"));
    }

    // --- HasNonContinuableFailures ---

    [Fact]
    public void HasNonContinuableFailures_EmptyList_ReturnsFalse()
    {
        Assert.False(InvokeHasNonContinuableFailures([]));
    }

    [Fact]
    public void HasNonContinuableFailures_AllContinuable_ReturnsFalse()
    {
        var steps = new List<PipelineStepRun>
        {
            new() { ContinueOnError = true },
            new() { ContinueOnError = true }
        };
        Assert.False(InvokeHasNonContinuableFailures(steps));
    }

    [Fact]
    public void HasNonContinuableFailures_OneNotContinuable_ReturnsTrue()
    {
        var steps = new List<PipelineStepRun>
        {
            new() { ContinueOnError = true },
            new() { ContinueOnError = false }
        };
        Assert.True(InvokeHasNonContinuableFailures(steps));
    }

    // --- Stage condition false → steps cancelled ---

    [Fact]
    public async Task AdvanceStageAsync_StageConditionFalse_CancelsSteps()
    {
        var yaml = """
            name: conditional
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: make
              - name: deploy
                agent: linux-01
                condition: "failed()"
                steps:
                  - name: ship
                    shell: deploy.sh
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });

        var deployStep = new PipelineStepRun { Id = 2, StageName = "deploy", StepName = "ship", PipelineRunId = 1 };
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([deployStep]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.HasAnyFailedStepInRunAsync(1, Arg.Any<CancellationToken>())
            .Returns(false);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Cancelled, deployStep.Status);
    }

    [Fact]
    public async Task AdvanceStageAsync_ConditionallySkippedStage_DispatchesItsDependentWithoutAnotherCallback()
    {
        var yaml = """
            name: conditional-chain
            trigger: manual
            stages:
              - name: source
                agent: linux-01
                steps:
                  - name: resolve
                    shell: git rev-parse HEAD
              - name: optional-artifact
                agent: linux-01
                depends_on: [source]
                condition: "eq(variables['USE_ARTIFACT'], 'true')"
                steps:
                  - name: restore
                    shell: restore.sh
              - name: deploy
                agent: linux-01
                depends_on: [optional-artifact]
                steps:
                  - name: ship
                    shell: deploy.sh
            """;

        var optional = new PipelineStepRun
        {
            Id = 2,
            StageName = "optional-artifact",
            StepName = "restore",
            PipelineRunId = 1
        };
        var deploy = new PipelineStepRun
        {
            Id = 3,
            StageName = "deploy",
            StepName = "ship",
            PipelineRunId = 1
        };
        var pendingCall = 0;
        var completedCall = 0;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "source", Arg.Any<CancellationToken>()).Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "source", Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            Status = PipelineStatus.Running,
            ResolvedVariablesJson = "{\"USE_ARTIFACT\":\"false\"}",
            Pipeline = new Pipeline { YamlDefinition = yaml }
        });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns(_ => ++pendingCall == 1 ? [optional] : [deploy]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns(_ => ++completedCall == 1
                ? [PipelineRunService.SystemPrepareStage, "source"]
                : [PipelineRunService.SystemPrepareStage, "source", "optional-artifact"]);
        _repoMock.GetTerminalStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns(_ => [PipelineRunService.SystemPrepareStage, "source", "optional-artifact"]);
        _repoMock.HasAnyFailedStepInRunAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01" });
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "source", TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Cancelled, optional.Status);
        Assert.Equal(2, pendingCall);
        Assert.True(completedCall >= 2);
        await _repoMock.Received(1).FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>());
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task => task.Name == "ship"));
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileRunAsync_ReadyPendingStage_DispatchesWithoutCompletionCallback()
    {
        var yaml = """
            name: release
            trigger: manual
            stages:
              - name: source
                agent: linux-01
                steps:
                  - name: resolve
                    shell: git rev-parse HEAD
              - name: CI
                agent: linux-01
                depends_on: [source]
                steps:
                  - name: launch-ci
                    shell: ci.sh
            """;
        var ciStep = new PipelineStepRun
        {
            Id = 3,
            StageName = "CI",
            StepName = "launch-ci",
            PipelineRunId = 220
        };
        var run = new PipelineRun
        {
            Id = 220,
            PipelineId = 1,
            Status = PipelineStatus.Running,
            ResolvedVariablesJson = "{}",
            Pipeline = new Pipeline { Id = 1, YamlDefinition = yaml }
        };

        _repoMock.GetPipelineRunWithPipelineAsync(220, Arg.Any<CancellationToken>()).Returns(run);
        _repoMock.GetPendingStepRunsAsync(220, Arg.Any<CancellationToken>()).Returns([ciStep]);
        _repoMock.GetCompletedStageNamesAsync(220, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "source"]);
        _repoMock.GetTerminalStageNamesAsync(220, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "source"]);
        _repoMock.HasAnyFailedStepInRunAsync(220, Arg.Any<CancellationToken>()).Returns(false);
        _repoMock.FindOnlineServerByAgentAsync("linux-01", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "linux-01" });
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.ReconcileRunAsync(220, TestContext.Current.CancellationToken);

        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task => task.Name == "launch-ci"));
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(220, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    // --- No server matched → run fails ---

    [Fact]
    public async Task ReconcileRunAsync_ConfiguredRunnerTemporarilyOffline_KeepsPreparePendingThenDispatches()
    {
        var yaml = """
            name: ci
            trigger: manual
            stages:
              - name: Compile
                os: linux
                steps:
                  - name: build
                    shell: dotnet build
            """;
        var prepare = new PipelineStepRun
        {
            Id = 1,
            PipelineRunId = 1,
            StageName = PipelineRunService.SystemPrepareStage,
            StepName = "Clone Repository",
            Status = TaskExecutionStatus.Pending,
            IsSystem = true
        };
        var run = new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            Status = PipelineStatus.Running,
            ResolvedVariablesJson = "{}",
            Pipeline = new Pipeline { Id = 1, YamlDefinition = yaml }
        };
        var onlineRunner = new Server { Id = 10, Name = "linux-01", OsType = OsType.Linux };

        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(run);
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns([prepare]);
        _repoMock.FindAnyOnlineRunnerAsync(Arg.Any<int?>(), OsType.Linux, Arg.Any<CancellationToken>())
            .Returns((Server?)null, onlineRunner);
        _repoMock.FindCandidateTargetServerIdsAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), OsType.Linux,
                Arg.Any<int?>(), false, Arg.Any<CancellationToken>())
            .Returns([10]);

        await _sut.ReconcileRunAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Pending, prepare.Status);
        _repoMock.DidNotReceive().TrackTask(Arg.Any<ServerTask>());
        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(
            1, PipelineStatus.Failed, Arg.Any<CancellationToken>());

        await _sut.ReconcileRunAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(TaskExecutionStatus.Assigned, prepare.Status);
        _repoMock.Received(1).TrackTask(Arg.Is<ServerTask>(task =>
            task.PipelineRunId == 1 && task.PipelineStepRunId == prepare.Id && task.ServerId == onlineRunner.Id));
    }

    [Fact]
    public async Task AdvanceStageAsync_NoServerMatched_FailsRun()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: make
              - name: deploy
                agent: nonexistent-agent
                steps:
                  - name: ship
                    shell: deploy.sh
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 2, StageName = "deploy", StepName = "ship", PipelineRunId = 1 }]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.HasAnyFailedStepInRunAsync(1, Arg.Any<CancellationToken>())
            .Returns(false);
        _repoMock.FindOnlineServerByAgentAsync("nonexistent-agent", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns((Server?)null);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_NoServerMatched_PersistsReasonViaTrackedRepoCall()
    {
        // Regression: the "no online server" reason must be persisted through the tracked
        // AppendRunWarningsAsync repo call. The previous code mutated the AsNoTracking entity
        // returned by GetPipelineRunWithPipelineAsync, so the run failed with no visible message.
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: make
              - name: deploy
                agent: nonexistent-agent
                steps:
                  - name: ship
                    shell: deploy.sh
            """;

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = yaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 2, StageName = "deploy", StepName = "ship", PipelineRunId = 1 }]);
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.HasAnyFailedStepInRunAsync(1, Arg.Any<CancellationToken>())
            .Returns(false);
        _repoMock.FindOnlineServerByAgentAsync("nonexistent-agent", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns((Server?)null);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.AppendRunWarningsAsync(1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "build", ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).AppendRunWarningsAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(w => w.Any(m => m.Contains("nonexistent-agent"))),
            Arg.Any<CancellationToken>());
    }

    // --- Dead-end runs fail explicitly instead of hanging in Running ---

    private const string DeadlockYaml = """
        name: dl
        trigger: manual
        stages:
          - name: A
            agent: x
            steps:
              - name: s1
                shell: echo a
          - name: B
            agent: x
            depends_on: [A]
            steps:
              - name: s2
                shell: echo b
        """;

    [Fact]
    public async Task AdvanceStageAsync_DeadlockedStage_FailsRunWithReason()
    {
        _repoMock.AreAllStepsInStageCompletedAsync(1, "A", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "A", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Running,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = DeadlockYaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 2, StageName = "B", StepName = "s2", PipelineRunId = 1 }]);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.AppendRunWarningsAsync(1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "A", ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).AppendRunWarningsAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(w => w.Any(m => m.Contains("depends on 'A'"))),
            Arg.Any<CancellationToken>());
        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAfterApprovalAsync_NoExecutableSteps_FailsRunWithReason()
    {
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(
                new PipelineRun
                {
                    Id = 1,
                    PipelineId = 1,
                    Status = PipelineStatus.WaitingForApproval,
                    ResolvedVariablesJson = "{}",
                    Pipeline = new Pipeline { YamlDefinition = DeadlockYaml }
                },
                new PipelineRun
                {
                    Id = 1,
                    PipelineId = 1,
                    Status = PipelineStatus.Running,
                    ResolvedVariablesJson = "{}",
                    Pipeline = new Pipeline { YamlDefinition = DeadlockYaml }
                });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.TryTransitionPipelineRunStatusAsync(
                1, PipelineStatus.WaitingForApproval, PipelineStatus.Running, Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.AppendRunWarningsAsync(1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repoMock.UpdatePipelineRunStatusAsync(1, Arg.Any<PipelineStatus>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.ResumeAfterApprovalAsync(1, ct: TestContext.Current.CancellationToken);

        await _repoMock.Received(1).AppendRunWarningsAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(w => w.Any(m => m.Contains("no executable steps"))),
            Arg.Any<CancellationToken>());
        await _repoMock.Received(1).UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceStageAsync_DeadlockButRunNotRunning_DoesNotFlipStatus()
    {
        _repoMock.AreAllStepsInStageCompletedAsync(1, "A", Arg.Any<CancellationToken>())
            .Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "A", Arg.Any<CancellationToken>())
            .Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Cancelled,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = DeadlockYaml }
            });
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>())
            .Returns([new PipelineStepRun { Id = 2, StageName = "B", StepName = "s2", PipelineRunId = 1 }]);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.AdvanceStageAsync(1, "A", ct: TestContext.Current.CancellationToken);

        await _repoMock.DidNotReceive().UpdatePipelineRunStatusAsync(1, PipelineStatus.Failed, Arg.Any<CancellationToken>());
        await _repoMock.DidNotReceive().AppendRunWarningsAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    // --- RetryFailedStepsAsync (partial re-run) ---

    [Fact]
    public async Task RetryFailedStepsAsync_RunNotFound_ReturnsNull()
    {
        _repoMock.GetPipelineRunWithPipelineAsync(99, Arg.Any<CancellationToken>())
            .Returns((PipelineRun?)null);

        Assert.Null(await _sut.RetryFailedStepsAsync(99, ct: TestContext.Current.CancellationToken));
        await _repoMock.DidNotReceive().ResetFailedStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryFailedStepsAsync_RunNotFailed_ReturnsNull()
    {
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun { Id = 1, PipelineId = 1, Status = PipelineStatus.Running, Pipeline = new Pipeline() });

        Assert.Null(await _sut.RetryFailedStepsAsync(1, ct: TestContext.Current.CancellationToken));
        await _repoMock.DidNotReceive().ResetFailedStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryFailedStepsAsync_NoFailedSteps_ReturnsNull()
    {
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Failed,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { YamlDefinition = "name: x" }
            });
        _repoMock.ResetFailedStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns(0);

        Assert.Null(await _sut.RetryFailedStepsAsync(1, ct: TestContext.Current.CancellationToken));
        await _repoMock.DidNotReceive().GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryFailedStepsAsync_FailedRunWithFailedSteps_ResetsAndReturnsRun()
    {
        var yaml = """
            name: x
            trigger: manual
            stages:
              - name: s
                agent: a
                steps:
                  - name: st
                    shell: echo hi
            """;
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Failed,
                ResolvedVariablesJson = "{}",
                Pipeline = new Pipeline { Id = 1, YamlDefinition = yaml }
            });
        _repoMock.ResetFailedStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns(1);
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetRunDetailAsync(1, Arg.Any<CancellationToken>())
            .Returns(new PipelineRun
            {
                Id = 1,
                PipelineId = 1,
                Status = PipelineStatus.Running,
                Pipeline = new Pipeline { Name = "CI" },
                StepRuns = [new PipelineStepRun { Id = 1, StepName = "st", StageName = "s" }]
            });

        var result = await _sut.RetryFailedStepsAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("CI", result.PipelineName);
        await _repoMock.Received(1).ResetFailedStepRunsAsync(1, Arg.Any<CancellationToken>());
    }

    // --- RerunAsync (G: rerun modes) ---

    private const string RerunSnapshotYaml = """
        name: snapshot
        trigger: manual
        stages:
          - name: build
            agent: linux-01
            steps:
              - name: compile
                shell: dotnet build
        """;

    [Fact]
    public async Task RerunAsync_SourceRunNotFound_ReturnsNull()
    {
        _repoMock.GetRunDetailAsync(99, Arg.Any<CancellationToken>()).Returns((PipelineRun?)null);

        Assert.Null(await _sut.RerunAsync(99, RerunMode.Current, ct: TestContext.Current.CancellationToken));
        await _repoMock.DidNotReceive().AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RerunAsync_SnapshotSameCommit_ReplaysSnapshotYamlPinnedToCommit()
    {
        var source = new PipelineRun
        {
            Id = 5,
            PipelineId = 1,
            Status = PipelineStatus.Success,
            YamlSnapshot = RerunSnapshotYaml,
            CommitHash = "abc123",
            AdditionalVariablesJson = "{}",
            Pipeline = new Pipeline { Name = "Deploy" },
            StepRuns = []
        };
        _repoMock.GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(source);
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = "name: live\ntrigger: manual\nstages: []", Runs = [] });
        _repoMock.AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.GetPendingStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await _sut.RerunAsync(5, RerunMode.SnapshotSameCommit, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        await _repoMock.Received(1).AddPipelineRunAsync(
            Arg.Is<PipelineRun>(r => r.YamlSnapshot == RerunSnapshotYaml && r.CommitHash == "abc123"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RerunAsync_Current_StartsFreshRunFromLiveDefinition()
    {
        const string liveYaml = """
            name: live
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
            """;
        var source = new PipelineRun
        {
            Id = 5,
            PipelineId = 1,
            Status = PipelineStatus.Failed,
            YamlSnapshot = RerunSnapshotYaml,
            CommitHash = "abc123",
            AdditionalVariablesJson = "{}",
            Pipeline = new Pipeline { Name = "Deploy" },
            StepRuns = []
        };
        _repoMock.GetRunDetailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(source);
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = liveYaml, Runs = [] });
        _repoMock.AddPipelineRunAsync(Arg.Any<PipelineRun>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repoMock.GetPendingStepRunsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await _sut.RerunAsync(5, RerunMode.Current, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        await _repoMock.Received(1).AddPipelineRunAsync(
            Arg.Is<PipelineRun>(r => r.YamlSnapshot == liveYaml),
            Arg.Any<CancellationToken>());
    }

    // --- Static helpers via reflection ---

    private static List<string> InvokeFindReadyStages(
        List<PipelineStepRun> pendingSteps, PipelineYamlDefinition definition,
        List<string> completedStages, List<string>? terminalStages = null)
    {
        var method = typeof(PipelineRunService).GetMethod("FindReadyStages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (List<string>)method.Invoke(null, [pendingSteps, definition, completedStages, terminalStages ?? completedStages])!;
    }

    private void InvokeMarkStepsAs(List<PipelineStepRun> steps, TaskExecutionStatus status)
    {
        var method = typeof(PipelineRunService).GetMethod("MarkStepsAs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        method.Invoke(_sut, [steps, status]);
    }

    private static Dictionary<string, string> InvokeResolveLegVariables(Dictionary<string, string> stageVars, string legKey, List<Dictionary<string, string>> matrixLegs)
        => PipelineRunHelpers.ResolveLegVariables(stageVars, legKey, matrixLegs);

    private static bool InvokeHasNonContinuableFailures(List<PipelineStepRun> failedSteps)
    {
        var method = typeof(PipelineRunService).GetMethod("HasNonContinuableFailures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (bool)method.Invoke(null, [failedSteps])!;
    }

    private static bool InvokeEvaluateCondition(string? condition, Dictionary<string, string> variables, List<string> completedStages, bool previousStageFailed)
        => PipelineRunHelpers.EvaluateCondition(condition, variables, completedStages, previousStageFailed);

    private static bool InvokeEvaluateVariableCondition(string condition, Dictionary<string, string> variables)
        => PipelineRunHelpers.EvaluateVariableCondition(condition, variables);

    private static Dictionary<string, string> InvokeParseOutputVariables(string? output)
        => PipelineRunHelpers.ParseOutputVariablesFromLogs(output);

    private static List<Dictionary<string, string>> InvokeExpandMatrix(Dictionary<string, List<string>>? matrix)
        => PipelineRunHelpers.ExpandMatrix(matrix);

    // --- EvaluateCondition ---

    [Fact]
    public void EvaluateCondition_NullOrEmpty_ReturnsTrue()
    {
        Assert.True(InvokeEvaluateCondition(null, [], [], false));
        Assert.True(InvokeEvaluateCondition("", [], [], false));
        Assert.True(InvokeEvaluateCondition("  ", [], [], false));
    }

    [Fact]
    public void EvaluateCondition_Always_ReturnsTrue()
    {
        Assert.True(InvokeEvaluateCondition("always()", [], [], true));
    }

    [Fact]
    public void EvaluateCondition_Succeeded_ReturnsTrueWhenNoFailure()
    {
        Assert.True(InvokeEvaluateCondition("succeeded()", [], [], false));
        Assert.False(InvokeEvaluateCondition("succeeded()", [], [], true));
    }

    [Fact]
    public void EvaluateCondition_Failed_ReturnsTrueWhenPreviousFailed()
    {
        Assert.True(InvokeEvaluateCondition("failed()", [], [], true));
        Assert.False(InvokeEvaluateCondition("failed()", [], [], false));
    }

    [Fact]
    public void EvaluateCondition_Cancelled_AlwaysReturnsFalse()
    {
        Assert.False(InvokeEvaluateCondition("cancelled()", [], [], false));
        Assert.False(InvokeEvaluateCondition("cancelled()", [], [], true));
    }

    [Fact]
    public void EvaluateCondition_VariableCondition_DelegatesToEvaluateVariableCondition()
    {
        var vars = new Dictionary<string, string> { ["env"] = "prod" };
        Assert.True(InvokeEvaluateCondition("eq(variables['env'], 'prod')", vars, [], false));
        Assert.False(InvokeEvaluateCondition("eq(variables['env'], 'dev')", vars, [], false));
    }

    // --- EvaluateVariableCondition ---

    [Fact]
    public void EvaluateVariableCondition_EqMatch_ReturnsTrue()
    {
        var vars = new Dictionary<string, string> { ["region"] = "us-east" };
        Assert.True(InvokeEvaluateVariableCondition("eq(variables['region'], 'us-east')", vars));
    }

    [Fact]
    public void EvaluateVariableCondition_EqMismatch_ReturnsFalse()
    {
        var vars = new Dictionary<string, string> { ["region"] = "eu-west" };
        Assert.False(InvokeEvaluateVariableCondition("eq(variables['region'], 'us-east')", vars));
    }

    [Fact]
    public void EvaluateVariableCondition_EqMissingVar_ReturnsFalse()
    {
        Assert.False(InvokeEvaluateVariableCondition("eq(variables['missing'], 'val')", new Dictionary<string, string>()));
    }

    [Fact]
    public void EvaluateVariableCondition_NeMatch_ReturnsTrue()
    {
        var vars = new Dictionary<string, string> { ["env"] = "dev" };
        Assert.True(InvokeEvaluateVariableCondition("ne(variables['env'], 'prod')", vars));
    }

    [Fact]
    public void EvaluateVariableCondition_NeMissingVar_ReturnsTrue()
    {
        Assert.True(InvokeEvaluateVariableCondition("ne(variables['missing'], 'val')", new Dictionary<string, string>()));
    }

    [Fact]
    public void EvaluateVariableCondition_UnknownFormat_ReturnsFalse()
    {
        // Fail-closed: unknown condition syntax should NOT permit the stage to run.
        Assert.False(InvokeEvaluateVariableCondition("something_unknown()", new Dictionary<string, string>()));
    }

    // --- ParseOutputVariables ---

    [Fact]
    public void ParseOutputVariables_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(InvokeParseOutputVariables(null));
        Assert.Empty(InvokeParseOutputVariables(""));
    }

    [Fact]
    public void ParseOutputVariables_SingleVariable_Parsed()
    {
        var output = "some log\n##aetheus[setvariable name=VERSION]1.2.3\nmore log";
        var result = InvokeParseOutputVariables(output);
        Assert.Single(result);
        Assert.Equal("1.2.3", result["VERSION"]);
    }

    [Fact]
    public void ParseOutputVariables_MultipleVariables_AllParsed()
    {
        var output = "##aetheus[setvariable name=A]valA\n##aetheus[setvariable name=B]valB";
        var result = InvokeParseOutputVariables(output);
        Assert.Equal(2, result.Count);
        Assert.Equal("valA", result["A"]);
        Assert.Equal("valB", result["B"]);
    }

    [Fact]
    public void ParseOutputVariables_NoMatch_ReturnsEmpty()
    {
        var result = InvokeParseOutputVariables("just normal output\nno special lines");
        Assert.Empty(result);
    }

    // --- ExpandMatrix ---

    [Fact]
    public void ExpandMatrix_NullOrEmpty_ReturnsSingleEmptyLeg()
    {
        var result = InvokeExpandMatrix(null);
        Assert.Single(result);
        Assert.Empty(result[0]);

        result = InvokeExpandMatrix(new Dictionary<string, List<string>>());
        Assert.Single(result);
        Assert.Empty(result[0]);
    }

    [Fact]
    public void ExpandMatrix_SingleDimension_ReturnsOneLegPerValue()
    {
        var matrix = new Dictionary<string, List<string>> { ["os"] = ["ubuntu", "windows"] };
        var result = InvokeExpandMatrix(matrix);
        Assert.Equal(2, result.Count);
        Assert.Equal("ubuntu", result[0]["os"]);
        Assert.Equal("windows", result[1]["os"]);
    }

    [Fact]
    public void ExpandMatrix_TwoDimensions_ReturnsCartesianProduct()
    {
        var matrix = new Dictionary<string, List<string>>
        {
            ["os"] = ["ubuntu", "windows"],
            ["arch"] = ["x64", "arm64"]
        };
        var result = InvokeExpandMatrix(matrix);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void ExpandMatrix_ThreeDimensions_ReturnsFullProduct()
    {
        var matrix = new Dictionary<string, List<string>>
        {
            ["os"] = ["a", "b"],
            ["arch"] = ["x", "y"],
            ["runtime"] = ["1", "2"]
        };
        var result = InvokeExpandMatrix(matrix);
        Assert.Equal(8, result.Count);
    }

    [Fact]
    public void ExpandMatrix_ProductBeyondLimit_IsRejectedBeforeMaterialization()
    {
        var matrix = new Dictionary<string, List<string>>
        {
            ["a"] = Enumerable.Range(1, 17).Select(i => $"a{i}").ToList(),
            ["b"] = Enumerable.Range(1, 17).Select(i => $"b{i}").ToList()
        };

        var error = Assert.Throws<ArgumentException>(() => InvokeExpandMatrix(matrix));

        Assert.Contains(PipelineRunHelpers.MaxMatrixLegs.ToString(), error.Message, StringComparison.Ordinal);
    }

    // --- S2 (audit 2026-07-06): ValidateMatrixValues (matrix values are shell-spliced) ---

    private static List<PipelineStageDefinition> StageWithMatrix(Dictionary<string, List<string>>? matrix) =>
        [new PipelineStageDefinition { Name = "build", Matrix = matrix }];

    [Theory]
    [InlineData("ubuntu-22.04")]
    [InlineData("net8.0")]
    [InlineData("linux/amd64")]
    [InlineData("node:20")]
    [InlineData("mcr.microsoft.com/dotnet/sdk:8.0")]
    [InlineData("key=value")]   // F-ENG-03: '=' now allowed (shell-inert in a bare word)
    [InlineData("50%")]         // F-ENG-03: '%' now allowed
    [InlineData("1.0,2.0")]     // F-ENG-03: ',' now allowed
    public void ValidateMatrixValues_LegitimateShapes_ReturnNoError(string value)
    {
        var errors = PipelineRunHelpers.ValidateMatrixValues(
            StageWithMatrix(new Dictionary<string, List<string>> { ["axis"] = [value] }));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("$(rm -rf /)")]           // command substitution
    [InlineData("a;whoami")]              // command separator
    [InlineData("a\nwhoami")]             // newline breaks out of the quoted context
    [InlineData("net8.0\n")]              // trailing newline (the ^...$ vs \A...\z bypass)
    [InlineData("-oProxyCommand=evil")]   // leading dash = argument injection
    [InlineData("`id`")]                  // backtick substitution
    [InlineData("Release --output /etc")] // space = shell word-splitting into extra arguments
    [InlineData("C:\\Windows\\System32")] // backslash = shell escape, still rejected (F-ENG-03)
    [InlineData("")]                      // empty value
    public void ValidateMatrixValues_ShellShapedValues_AreRejected(string value)
    {
        var errors = PipelineRunHelpers.ValidateMatrixValues(
            StageWithMatrix(new Dictionary<string, List<string>> { ["axis"] = [value] }));

        var error = Assert.Single(errors);
        Assert.Contains("axis", error);
        Assert.Contains("build", error);
    }

    [Fact]
    public void ValidateMatrixValues_NoMatrix_ReturnsNoError()
    {
        Assert.Empty(PipelineRunHelpers.ValidateMatrixValues(StageWithMatrix(null)));
        Assert.Empty(PipelineRunHelpers.ValidateMatrixValues(
            StageWithMatrix(new Dictionary<string, List<string>>())));
    }

    [Fact]
    public void ValidateMatrixValues_ReportsEveryOffendingValue()
    {
        var errors = PipelineRunHelpers.ValidateMatrixValues(
            StageWithMatrix(new Dictionary<string, List<string>>
            {
                ["os"] = ["ubuntu", "a;b"],
                ["arch"] = ["`x`"]
            }));

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void ValidateMatrixValues_EnforcesAxesValuesAndProductLimits()
    {
        var tooManyAxes = Enumerable.Range(1, PipelineRunHelpers.MaxMatrixAxes + 1)
            .ToDictionary(i => $"axis{i}", _ => new List<string> { "one" });
        var tooManyValues = Enumerable.Range(1, PipelineRunHelpers.MaxMatrixValuesPerAxis + 1)
            .Select(i => $"v{i}").ToList();
        var excessiveProduct = new Dictionary<string, List<string>>
        {
            ["a"] = Enumerable.Range(1, 17).Select(i => $"a{i}").ToList(),
            ["b"] = Enumerable.Range(1, 17).Select(i => $"b{i}").ToList()
        };

        Assert.Contains(PipelineRunHelpers.ValidateMatrixValues(StageWithMatrix(tooManyAxes)),
            e => e.Contains("axes", StringComparison.Ordinal));
        Assert.Contains(PipelineRunHelpers.ValidateMatrixValues(
                StageWithMatrix(new Dictionary<string, List<string>> { ["axis"] = tooManyValues })),
            e => e.Contains("values", StringComparison.Ordinal));
        Assert.Contains(PipelineRunHelpers.ValidateMatrixValues(StageWithMatrix(excessiveProduct)),
            e => e.Contains("legs", StringComparison.Ordinal));
    }

    // --- F-EXEC-1: ResolveCandidateTargetServerIdsAsync (authorization target set) ---

    [Fact]
    public async Task ResolveCandidateTargetServerIds_AgentSelector_ReturnsAgentMatchedIds()
    {
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
            """;
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = yaml, Runs = [] });
        _repoMock.FindCandidateTargetServerIdsAsync(
                null, null, "linux-01", OsType.Unknown, null, false, Arg.Any<CancellationToken>())
            .Returns([7, 9]);

        var ids = await _sut.ResolveCandidateTargetServerIdsAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 7, 9 }, ids.OrderBy(i => i).ToArray());
        await _repoMock.Received(1).FindCandidateTargetServerIdsAsync(
            null, null, "linux-01", OsType.Unknown, null, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveCandidateTargetServerIds_PoolTakesPrecedenceOverAgent()
    {
        // Mirrors ResolveServerAsync precedence: pool > environment > agent. When a stage sets
        // both, only the pool enumerator must be consulted (the agent set must NOT widen/leak).
        var yaml = """
            name: deploy
            trigger: manual
            stages:
              - name: build
                pool: prod
                agent: linux-01
                steps:
                  - name: compile
                    shell: dotnet build
            """;
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Deploy", YamlDefinition = yaml, Runs = [] });
        _repoMock.FindCandidateTargetServerIdsAsync(
                "prod", null, "linux-01", OsType.Unknown, null, false, Arg.Any<CancellationToken>())
            .Returns([3]);

        var ids = await _sut.ResolveCandidateTargetServerIdsAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 3 }, ids.ToArray());
        await _repoMock.Received(1).FindCandidateTargetServerIdsAsync(
            "prod", null, "linux-01", OsType.Unknown, null, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveCandidateTargetServerIds_InvalidYaml_IsRejected()
    {
        _repoMock.FindPipelineAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Pipeline { Id = 1, Name = "Bad", YamlDefinition = "{{invalid", Runs = [] });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ResolveCandidateTargetServerIdsAsync(1, ct: TestContext.Current.CancellationToken));
    }

    private void ArrangeAdvanceToStage(string yaml, string stageName, params PipelineStepRun[] steps)
        => ArrangeAdvanceToStageWithVariables(yaml, stageName, "{}", steps);

    private void ArrangeAdvanceToStageWithVariables(
        string yaml, string stageName, string resolvedVariablesJson, params PipelineStepRun[] steps)
    {
        var run = new PipelineRun
        {
            Id = 1,
            PipelineId = 1,
            AdditionalVariablesJson = resolvedVariablesJson,
            ResolvedVariablesJson = resolvedVariablesJson,
            Pipeline = new Pipeline { Id = 1, YamlDefinition = yaml }
        };
        var server = new Server
        {
            Id = 10,
            Name = "linux-01",
            OsType = OsType.Linux,
            DeploymentTargetAvailable = true
        };

        _repoMock.AreAllStepsInStageCompletedAsync(1, "build", Arg.Any<CancellationToken>()).Returns(true);
        _repoMock.GetFailedStepRunsInStageAsync(1, "build", Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.GetPipelineRunWithPipelineAsync(1, Arg.Any<CancellationToken>()).Returns(run);
        _repoMock.GetPendingStepRunsAsync(1, Arg.Any<CancellationToken>()).Returns(steps.ToList());
        _repoMock.GetCompletedStageNamesAsync(1, Arg.Any<CancellationToken>())
            .Returns([PipelineRunService.SystemPrepareStage, "build"]);
        _repoMock.GetActiveStageNamesAsync(1, Arg.Any<CancellationToken>()).Returns([]);
        _repoMock.FindOnlineServerByAgentAsync(Arg.Any<string>(), Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(server);
        _repoMock.FindAnyOnlineRunnerAsync(Arg.Any<int?>(), Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(server);
        _repoMock.FindOnlineDeployTargetAsync(
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<OsType>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(server);
        _repoMock.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    private void ArrangeEnvironment(params EnvironmentCheck[] checks)
    {
        _repoMock.FindEnvironmentByNameAsync("prod", Arg.Any<CancellationToken>())
            .Returns(new Aetheus.Back.Data.Entities.Environment { Id = 6, Name = "prod", RequireApproval = false });
        _repoMock.GetEnvironmentChecksAsync(6, Arg.Any<CancellationToken>()).Returns(checks.ToList());
        _repoMock.FindOnlineServerInEnvironmentAsync("prod", Arg.Any<OsType>(), Arg.Any<CancellationToken>())
            .Returns(new Server { Id = 10, Name = "prod-01", OsType = OsType.Linux });
    }

    private static string EnvironmentStageYaml() => """
        name: release
        trigger: manual
        stages:
          - name: build
            agent: linux-01
            steps:
              - name: compile
                shell: dotnet build
          - name: ship
            environment: prod
            steps:
              - name: publish
                shell: echo publish
        """;

    private sealed class StaticResponseHandler(System.Net.HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
