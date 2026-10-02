// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Text.Json;
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// The architecture metrics report of candidate #2328 carried 39,240 records and took 130 s to publish
/// (PLAN-007 lot 6). This ingests a report of that size on PostgreSQL, twice on the default branch so
/// the second one also resolves a baseline, and proves every record lands; the durations are written
/// to the test output as the measurement.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AnalysisMetricIngestionIntegrationTests(PostgresFixture fixture) : RelationalTestBase(fixture)
{
    private const int MetricCount = 39_240;

    [Fact]
    public async Task ArchitectureSizedMetricsReport_PersistsEveryRecordWithItsBaseline()
    {
        await ResetAndMigrateAsync();
        var ct = TestContext.Current.CancellationToken;
        var pipelineId = await SeedPipelineAsync(ct);

        var firstRun = await SeedRunAsync(pipelineId, ct);
        var first = await PublishTimedAsync(firstRun, "first", ct);
        await using (var db = NewContext())
        {
            await db.PipelineRuns.Where(run => run.Id == firstRun.RunId)
                .ExecuteUpdateAsync(set => set.SetProperty(run => run.Status, PipelineStatus.Success), ct);
        }
        var secondRun = await SeedRunAsync(pipelineId, ct);
        var second = await PublishTimedAsync(secondRun, "second (with baseline)", ct);

        await using var verification = NewContext();
        Assert.Equal(MetricCount, await verification.AnalysisMetrics.CountAsync(m => m.AnalysisReportId == first.Id, ct));
        Assert.Equal(MetricCount, await verification.AnalysisMetrics.CountAsync(m => m.AnalysisReportId == second.Id, ct));
        Assert.Equal(MetricCount, await verification.AnalysisMetrics
            .CountAsync(m => m.AnalysisReportId == second.Id && m.BaselineValue != null, ct));
    }

    private async Task<AnalysisReportDto> PublishTimedAsync((int RunId, int ArtifactId) run, string label, CancellationToken ct)
    {
        await using var db = NewContext();
        var service = CreateService(db);
        var stopwatch = Stopwatch.StartNew();
        var report = await service.PublishReportAsync(run.RunId, Request(run.ArtifactId), ct);
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Ingestion {label}: {MetricCount} metrics in {stopwatch.Elapsed.TotalSeconds:F2} s");
        return report;
    }

    private static PublishAnalysisReportRequest Request(int artifactId) => new()
    {
        ScannerKey = "dotnet-architecture-metrics",
        ScannerName = "Microsoft.CodeAnalysis Architecture",
        ScannerVersion = "4.14.0",
        Category = AnalysisCategory.Architecture,
        Status = AnalysisReportStatus.Passed,
        Format = AnalysisReportFormat.MetricsJson,
        StartedAt = DateTime.UtcNow.AddMinutes(-1),
        CompletedAt = DateTime.UtcNow,
        ReportPath = "dotnet-architecture-metrics.json",
        PipelineArtifactId = artifactId,
        ReportContent = JsonSerializer.Serialize(new
        {
            // The same shape ArchitectureAnalysisPublisher emits: one record per dependency edge.
            metrics = Enumerable.Range(0, MetricCount).Select(index => new
            {
                key = "architecture.dependencies.namespace",
                value = 1.0,
                unit = "edge",
                scope = "namespace-edge",
                language = "csharp",
                filePath = $"src/Aetheus.Back/Components/Module{index % 400}/File{index}.cs",
                symbol = $"Aetheus.Back.Components.Module{index % 400}->Aetheus.Shared.Target{index}",
                toolName = "Microsoft.CodeAnalysis Architecture",
                direction = "Informational"
            })
        })
    };

    private async Task<int> SeedPipelineAsync(CancellationToken ct)
    {
        await using var db = NewContext();
        var organization = new Organization { Name = "Ingestion", Slug = $"ingestion-{Guid.NewGuid():N}" };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(ct);
        var project = new Project { Name = "Project", OrganizationId = organization.Id, DefaultBranch = "main" };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        var pipeline = new Pipeline { Name = "Quality", ProjectId = project.Id, YamlDefinition = "name: quality\ntrigger: manual\nstages: []" };
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync(ct);
        return pipeline.Id;
    }

    private async Task<(int RunId, int ArtifactId)> SeedRunAsync(int pipelineId, CancellationToken ct)
    {
        await using var db = NewContext();
        var run = new PipelineRun
        {
            PipelineId = pipelineId,
            Status = PipelineStatus.Running,
            StartedAt = DateTime.UtcNow,
            BranchName = "main",
            CommitHash = new string('a', 40)
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);
        var artifact = new PipelineArtifact
        {
            PipelineId = pipelineId,
            PipelineRunId = run.Id,
            Name = "analysis-dotnet-architecture-metrics",
            FilePath = "dotnet-architecture-metrics.json"
        };
        db.PipelineArtifacts.Add(artifact);
        await db.SaveChangesAsync(ct);
        return (run.Id, artifact.Id);
    }

    private static AnalysisService CreateService(AppDbContext db)
    {
        var repository = new AnalysisRepository(db);
        var runtime = Options.Create(new AnalysisPlatformOptions());
        return new AnalysisService(
            repository,
            new AnalysisIngestGate(runtime),
            new AnalysisPolicyEngine(repository),
            TimeProvider.System,
            Substitute.For<IAuditService>(),
            new DbTransactionScope(db),
            Substitute.For<IEntityChangeNotifier>(),
            Substitute.For<INotificationService>(),
            Substitute.For<IDependencyTrackOutbox>(),
            runtime,
            Options.Create(new DependencyTrackOptions()));
    }
}
