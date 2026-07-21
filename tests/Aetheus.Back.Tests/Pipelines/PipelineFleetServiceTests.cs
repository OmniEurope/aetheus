// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Shared.DTOs;
using NSubstitute;

namespace Aetheus.Back.Tests;

public sealed class PipelineFleetServiceTests
{
    private readonly FakePipelineFleetRepository _fleetRepository = new();
    private readonly IPipelineRepository _pipelineRepository = Substitute.For<IPipelineRepository>();
    private readonly IPipelineService _pipelineService = Substitute.For<IPipelineService>();
    private readonly IPipelineTemplateService _templateService = Substitute.For<IPipelineTemplateService>();
    private readonly IPipelineTemplateResolver _resolver = Substitute.For<IPipelineTemplateResolver>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly PipelineFleetService _service;

    public PipelineFleetServiceTests()
    {
        _resolver.ResolveAsync(
                Arg.Is<string>(yaml => yaml.StartsWith("name: template-resolution", StringComparison.Ordinal)),
                4,
                Arg.Is<IReadOnlyDictionary<string, string>?>(parameters => parameters == null),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<string>(0).Contains("ci@1", StringComparison.Ordinal)
                ? Resolution(Template().Versions.Single(version => version.Version == 1).YamlContent)
                : Resolution(Template().Versions.Single(version => version.Version == 2).YamlContent));
        _service = new PipelineFleetService(
            _fleetRepository, _pipelineRepository, _pipelineService, _templateService, _resolver, _audit);
    }

    [Fact]
    public async Task GetAsync_ComputesCurrentOutdatedAndOffCatalog()
    {
        _pipelineRepository.GetTemplatesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new PipelineTemplate { Id = 10, Name = "ci", OrganizationId = 4, LatestVersion = 3 }
        ]);
        _fleetRepository.Rows =
        [
            Row(1, "current", "extends: ci@3"),
            Row(2, "outdated", "extends: ci@1"),
            Row(3, "free", "name: free\nstages: []")
        ];

        var result = await _service.GetAsync(new PipelineFleetPaginationRequest(), null, null, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(PipelineFleetFreshness.Current, result.Items.Single(item => item.PipelineId == 1).Freshness);
        Assert.Equal(PipelineFleetFreshness.Outdated, result.Items.Single(item => item.PipelineId == 2).Freshness);
        Assert.Equal(PipelineFleetFreshness.OffCatalog, result.Items.Single(item => item.PipelineId == 3).Freshness);
    }

    [Fact]
    public async Task GetAsync_WhenCandidateSafetyBoundIsExceeded_RequiresNarrowerFilter()
    {
        _pipelineRepository.GetTemplatesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _fleetRepository.Rows = Enumerable.Range(1, PipelineFleetRepository.MaxCandidateCount + 1)
            .Select(id => Row(id, $"pipeline-{id}", "stages: []"))
            .ToList();

        var exception = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.GetAsync(new PipelineFleetPaginationRequest(), null, null, ct: TestContext.Current.CancellationToken));

        Assert.Contains(PipelineFleetRepository.MaxCandidateCount.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("Narrow", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_SortsGloballyBeforeTakingRequestedPage()
    {
        _pipelineRepository.GetTemplatesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _fleetRepository.Rows =
        [
            Row(1, "alpha", "stages: []"),
            Row(2, "zulu", "stages: []")
        ];

        var result = await _service.GetAsync(new PipelineFleetPaginationRequest
        {
            Page = 1,
            PageSize = 1,
            SortBy = "PipelineName",
            SortDescending = true
        }, null, null, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal("zulu", Assert.Single(result.Items).PipelineName);
    }

    [Fact]
    public async Task PreviewUpdateAsync_ReturnsResolvedDiffAndOrphanOverride()
    {
        var row = Row(2, "outdated", "name: child\nextends: ci@1\nstages:\n  - name: build\n    steps:\n      - name: removed\n        shell: echo local");
        _fleetRepository.Rows = [row];
        _pipelineRepository.FindTemplateByNameAsync("ci", 4, Arg.Any<CancellationToken>()).Returns(Template());
        _resolver.ResolveAsync(Arg.Is<string>(yaml => yaml.Contains("ci@1")), 4,
                Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<string>(0).StartsWith("name: template-resolution", StringComparison.Ordinal)
                ? Resolution(Template().Versions.Single(version => version.Version == 1).YamlContent)
                : Resolution("old resolved"));
        _resolver.ResolveAsync(Arg.Is<string>(yaml => yaml.Contains("ci@2")), 4,
                Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<string>(0).StartsWith("name: template-resolution", StringComparison.Ordinal)
                ? Resolution(Template().Versions.Single(version => version.Version == 2).YamlContent)
                : Resolution("new resolved"));

        var result = await _service.PreviewUpdateAsync(2, 2, ct: TestContext.Current.CancellationToken);

        Assert.Equal("old resolved", result.CurrentResolvedYaml);
        Assert.Equal("new resolved", result.TargetResolvedYaml);
        Assert.Contains("stage:build/step:removed", result.OrphanOverrides);
    }

    [Fact]
    public async Task UpdateAsync_OrphanWithoutAcknowledgement_IsRejectedBeforeWrite()
    {
        var row = Row(2, "outdated", "name: child\nextends: ci@1\nstages:\n  - name: build\n    steps:\n      - name: removed\n        shell: echo local");
        _fleetRepository.Rows = [row];
        _pipelineRepository.FindTemplateByNameAsync("ci", 4, Arg.Any<CancellationToken>()).Returns(Template());
        _resolver.ResolveAsync(Arg.Any<string>(), 4, Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>()).Returns(call =>
            call.ArgAt<string>(0).StartsWith("name: template-resolution", StringComparison.Ordinal)
                ? Resolution(call.ArgAt<string>(0).Contains("ci@1", StringComparison.Ordinal)
                    ? Template().Versions.Single(version => version.Version == 1).YamlContent
                    : Template().Versions.Single(version => version.Version == 2).YamlContent)
                : Resolution("resolved"));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.UpdateAsync(2, new PipelineFleetUpdateRequest
            {
                TargetVersion = 2,
                ExpectedSourceYamlHash = Hash(row.YamlDefinition)
            }, ct: TestContext.Current.CancellationToken));

        await _pipelineService.DidNotReceive().UpdatePipelineAsync(
            Arg.Any<int>(), Arg.Any<UpdatePipelineRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_Acknowledged_RewritesPinThroughPipelineService()
    {
        var row = Row(2, "outdated", "name: child\nextends: ci@1\nstages: []");
        _fleetRepository.Rows = [row];
        _pipelineRepository.FindTemplateByNameAsync("ci", 4, Arg.Any<CancellationToken>()).Returns(Template());
        _resolver.ResolveAsync(Arg.Any<string>(), 4, Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>()).Returns(call =>
            call.ArgAt<string>(0).StartsWith("name: template-resolution", StringComparison.Ordinal)
                ? Resolution(call.ArgAt<string>(0).Contains("ci@1", StringComparison.Ordinal)
                    ? Template().Versions.Single(version => version.Version == 1).YamlContent
                    : Template().Versions.Single(version => version.Version == 2).YamlContent)
                : Resolution("resolved"));
        _pipelineService.UpdatePipelineAsync(2, Arg.Any<UpdatePipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineDto { Id = 2 });

        await _service.UpdateAsync(2, new PipelineFleetUpdateRequest
        {
            TargetVersion = 2,
            AcknowledgeOrphanOverrides = true,
            ExpectedSourceYamlHash = Hash(row.YamlDefinition)
        }, ct: TestContext.Current.CancellationToken);

        await _pipelineService.Received(1).UpdatePipelineAsync(2,
            Arg.Is<UpdatePipelineRequest>(request => request.YamlDefinition.Contains("ci@2")),
            Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(
            "TemplateVersionAdopted", "Pipeline", 2,
            Arg.Is<string>(details => details.Contains("v1 -> v2", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_StalePreviewHash_IsRejectedBeforeWrite()
    {
        var row = Row(2, "outdated", "name: child\nextends: ci@1\nstages: []");
        _fleetRepository.Rows = [row];
        _pipelineRepository.FindTemplateByNameAsync("ci", 4, Arg.Any<CancellationToken>())
            .Returns(Template());
        _resolver.ResolveAsync(
                Arg.Any<string>(), 4, Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<string>(0).StartsWith("name: template-resolution", StringComparison.Ordinal)
                ? Resolution(call.ArgAt<string>(0).Contains("ci@1", StringComparison.Ordinal)
                    ? Template().Versions.Single(version => version.Version == 1).YamlContent
                    : Template().Versions.Single(version => version.Version == 2).YamlContent)
                : Resolution("resolved"));

        await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(2,
            new PipelineFleetUpdateRequest
            {
                TargetVersion = 2,
                ExpectedSourceYamlHash = new string('A', 64)
            }, ct: TestContext.Current.CancellationToken));

        await _pipelineService.DidNotReceive().UpdatePipelineAsync(
            Arg.Any<int>(), Arg.Any<UpdatePipelineRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_CreatesVersionOneAndOptionallyRewritesPipelineToPin()
    {
        var row = Row(3, "free", "name: free\nstages: []");
        _fleetRepository.Rows = [row];
        _templateService.CreateTemplateAsync(Arg.Any<CreatePipelineTemplateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineTemplateDto { Id = 12, Name = "shared-ci", Version = 1 });
        _pipelineService.UpdatePipelineAsync(3, Arg.Any<UpdatePipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineDto { Id = 3 });

        var result = await _service.ExtractAsync(3, new ExtractPipelineTemplateRequest
        {
            TemplateName = "shared-ci",
            Category = "CI",
            RewritePipeline = true
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Version);
        await _templateService.Received(1).CreateTemplateAsync(
            Arg.Is<CreatePipelineTemplateRequest>(request => request.OrganizationId == 4
                && request.YamlContent == row.YamlDefinition), Arg.Any<CancellationToken>());
        await _pipelineService.Received(1).UpdatePipelineAsync(3,
            Arg.Is<UpdatePipelineRequest>(request => request.YamlDefinition.Contains("shared-ci@1")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteAsync_PublishesNextVersionAndRebasesSourceWhenRequested()
    {
        var row = Row(2, "divergent", "name: child\nextends: ci@1\nstages: []");
        _fleetRepository.Rows = [row];
        _pipelineRepository.FindTemplateByNameAsync("ci", 4, Arg.Any<CancellationToken>()).Returns(Template());
        _templateService.UpdateTemplateAsync(10, Arg.Any<UpdatePipelineTemplateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineTemplateDto { Id = 10, Name = "ci", Version = 3 });
        _pipelineService.UpdatePipelineAsync(2, Arg.Any<UpdatePipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineDto { Id = 2 });

        var result = await _service.PromoteAsync(2, new PromotePipelineTemplateRequest
        {
            YamlContent = "name: ci\nstages: []",
            ChangelogEntry = "Adopt improvement",
            RebaseSourcePipeline = true
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Version);
        await _templateService.Received(1).UpdateTemplateAsync(10,
            Arg.Is<UpdatePipelineTemplateRequest>(request => request.ChangelogEntry == "Adopt improvement"),
            Arg.Any<CancellationToken>());
        await _pipelineService.Received(1).UpdatePipelineAsync(2,
            Arg.Is<UpdatePipelineRequest>(request => request.YamlDefinition.Contains("ci@3")
                && !request.YamlDefinition.Contains("echo local")), Arg.Any<CancellationToken>());
    }

    private static PipelineFleetRow Row(int id, string name, string yaml) =>
        new(id, name, "description", yaml, "main", 5, 5, null, null, "Toto", "Project", 4);

    private static string Hash(string yaml) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(yaml)));

    private static PipelineTemplate Template() => new()
    {
        Id = 10,
        Name = "ci",
        OrganizationId = 4,
        LatestVersion = 2,
        Versions =
        [
            new PipelineTemplateVersion
            {
                Version = 1,
                YamlContent = "name: ci\nstages:\n  - name: build\n    steps:\n      - name: removed\n        shell: echo old"
            },
            new PipelineTemplateVersion
            {
                Version = 2,
                YamlContent = "name: ci\nstages:\n  - name: build\n    steps:\n      - name: replacement\n        shell: echo new"
            }
        ]
    };

    private static PipelineTemplateResolution Resolution(string yaml) =>
        new(new PipelineYamlDefinition { Name = "resolved" }, yaml, "ci", 1, 1, false);

    private sealed class FakePipelineFleetRepository : IPipelineFleetRepository
    {
        public List<PipelineFleetRow> Rows { get; set; } = [];

        public Task<List<PipelineFleetRow>> GetCandidatesAsync(
            IReadOnlyCollection<int>? organizationIds, IReadOnlyCollection<int>? accessiblePipelineIds,
            string? search, int? projectId, CancellationToken ct) =>
            Task.FromResult(Rows.Where(row => organizationIds is null || organizationIds.Contains(row.OrganizationId))
                .Where(row => accessiblePipelineIds is null || accessiblePipelineIds.Contains(row.PipelineId))
                .Where(row => projectId is null || row.OwnerProjectId == projectId)
                .ToList());

        public Task<PipelineFleetRow?> GetAsync(int pipelineId, CancellationToken ct) =>
            Task.FromResult(Rows.FirstOrDefault(row => row.PipelineId == pipelineId));
    }
}
