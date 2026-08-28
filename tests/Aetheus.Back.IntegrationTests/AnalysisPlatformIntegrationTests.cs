// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Aetheus.Back.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AnalysisPlatformIntegrationTests(PostgresFixture fixture) : RelationalTestBase(fixture)
{
    [Fact]
    public async Task ConcurrentIdenticalIngestionAcrossBackendInstancesConvergesToOneReport()
    {
        await ResetAndMigrateAsync();
        var (runId, artifactId) = await SeedRunAsync();
        var gate = new AsyncArrivalGate(2);
        await using var firstContext = NewContext();
        await using var secondContext = NewContext();
        var first = CreateService(firstContext, gate);
        var second = CreateService(secondContext, gate);
        var request = Request(artifactId);

        var reports = await Task.WhenAll(
            first.PublishReportAsync(runId, request, TestContext.Current.CancellationToken),
            second.PublishReportAsync(runId, request, TestContext.Current.CancellationToken));

        Assert.Equal(reports[0].Id, reports[1].Id);
        await using var verification = NewContext();
        Assert.Equal(1, await verification.AnalysisReports.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verification.AnalysisFindings.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verification.AnalysisFindingOccurrences.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verification.AnalysisEvaluations.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FingerprintUniquenessAndQueriesRemainProjectScoped()
    {
        await ResetAndMigrateAsync();
        await using var db = NewContext();
        var firstOrg = new Organization { Name = "First", Slug = $"first-{Guid.NewGuid():N}" };
        var secondOrg = new Organization { Name = "Second", Slug = $"second-{Guid.NewGuid():N}" };
        db.Organizations.AddRange(firstOrg, secondOrg);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var firstProject = new Project { Name = "First", OrganizationId = firstOrg.Id };
        var secondProject = new Project { Name = "Second", OrganizationId = secondOrg.Id };
        db.Projects.AddRange(firstProject, secondProject);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.AnalysisFindings.AddRange(
            Finding(firstOrg.Id, firstProject.Id, "same-fingerprint"),
            Finding(secondOrg.Id, secondProject.Id, "same-fingerprint"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new AnalysisRepository(db);
        var first = await repository.GetFindingsByFingerprintsAsync(
            firstProject.Id, ["same-fingerprint"], TestContext.Current.CancellationToken);
        var second = await repository.GetFindingsByFingerprintsAsync(
            secondProject.Id, ["same-fingerprint"], TestContext.Current.CancellationToken);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(firstOrg.Id, first.Values.Single().OrganizationId);
        Assert.Equal(secondOrg.Id, second.Values.Single().OrganizationId);
    }

    [Fact]
    public async Task AggregateRunGate_FailsClosedAndKeepsFindingLinks()
    {
        await ResetAndMigrateAsync();
        var (runId, _) = await SeedRunAsync();
        await using var db = NewContext();
        var run = await db.PipelineRuns.Include(item => item.Pipeline).ThenInclude(item => item.Project)
            .SingleAsync(item => item.Id == runId, TestContext.Current.CancellationToken);
        var organizationId = run.Pipeline.Project!.OrganizationId;
        var projectId = run.Pipeline.ProjectId!.Value;
        var finding = Finding(organizationId, projectId, "aggregate-fingerprint");
        var report = new AnalysisReport
        {
            OrganizationId = organizationId,
            ProjectId = projectId,
            PipelineRunId = runId,
            ScannerKey = "opengrep",
            ScannerName = "OpenGrep",
            ScannerVersion = "1.22.0",
            Category = AnalysisCategory.Sast,
            Status = AnalysisReportStatus.Failed,
            Format = AnalysisReportFormat.Sarif,
            ContentHash = new string('b', 64),
            ContentSize = 123,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            CompletedAt = DateTime.UtcNow,
            Occurrences =
            [
                new AnalysisFindingOccurrence
                {
                    AnalysisFinding = finding,
                    ScannerKey = "opengrep",
                    ToolName = "OpenGrep",
                    RuleId = "security.rule",
                    Message = "unsafe-old",
                    FilePath = "src/Old.cs",
                    StartLine = 7,
                    LocationHash = "location-old",
                    IsNew = true,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1)
                },
                new AnalysisFindingOccurrence
                {
                    AnalysisFinding = finding,
                    ScannerKey = "opengrep",
                    ToolName = "OpenGrep",
                    RuleId = "security.rule",
                    Message = "unsafe-new",
                    FilePath = "src/New.cs",
                    StartLine = 42,
                    LocationHash = "location-new",
                    IsNew = false,
                    CreatedAt = DateTime.UtcNow
                }
            ],
            Evaluation = new AnalysisEvaluation
            {
                OrganizationId = organizationId,
                ProjectId = projectId,
                PipelineRunId = runId,
                Status = AnalysisGateStatus.Blocked,
                PolicySnapshotHash = new string('c', 64),
                BlockerCount = 1,
                EvaluatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            }
        };
        db.AnalysisReports.Add(report);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var gate = await new AnalysisRepository(db).GetRunGateAsync(runId, TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisGateStatus.Blocked, gate.Status);
        Assert.Equal(1, gate.BlockerCount);
        var projectedFinding = Assert.Single(gate.Findings);
        Assert.Equal(finding.Id, projectedFinding.FindingId);
        Assert.Equal("rule", projectedFinding.RuleId);
        Assert.Equal("Finding", projectedFinding.Title);
        Assert.Equal("Finding", projectedFinding.Message);
        Assert.Equal(AnalysisCategory.Sast, projectedFinding.Category);
        Assert.Equal(AnalysisFindingStatus.Open, projectedFinding.Status);
        Assert.Equal("src/New.cs", projectedFinding.FilePath);
        Assert.Equal(42, projectedFinding.StartLine);
        Assert.True(projectedFinding.IsNew);

        var server = new Server
        {
            Name = "analysis-runner",
            Hostname = "analysis-runner.local",
            OrganizationId = organizationId
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Tasks.AddRange(
            new ServerTask
            {
                ServerId = server.Id,
                PipelineRunId = runId,
                Name = "OpenGrep",
                Command = "opengrep",
                Operation = OperationKind.PipelineRunScanner
            },
            new ServerTask
            {
                ServerId = server.Id,
                PipelineRunId = runId,
                Name = "Trivy",
                Command = "trivy-dependencies",
                Operation = OperationKind.PipelineRunScanner
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        gate = await new AnalysisRepository(db).GetRunGateAsync(
            runId, TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisGateStatus.Error, gate.Status);
        Assert.Contains(gate.MissingProducers,
            item => item.StartsWith("trivy-dependencies:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SbomPublication_AtomicallyEnqueuesOneDurableDependencyTrackItem()
    {
        await ResetAndMigrateAsync();
        var (runId, artifactId) = await SeedRunAsync();
        await using var db = NewContext();
        var repository = new AnalysisRepository(db);
        var runtime = Options.Create(new AnalysisPlatformOptions());
        var dependencyOptions = Options.Create(new DependencyTrackOptions { Enabled = true });
        var service = new AnalysisService(
            repository,
            new AnalysisIngestGate(runtime),
            new AnalysisPolicyEngine(repository),
            TimeProvider.System,
            Substitute.For<IAuditService>(),
            new DbTransactionScope(db),
            Substitute.For<IEntityChangeNotifier>(),
            Substitute.For<INotificationService>(),
            new DependencyTrackOutbox(
                new DependencyTrackOutboxRepository(db),
                dependencyOptions,
                TimeProvider.System),
            runtime,
            dependencyOptions);
        var request = Request(artifactId) with
        {
            ScannerKey = "syft",
            ScannerName = "Syft",
            Category = AnalysisCategory.Sbom,
            Status = AnalysisReportStatus.Passed,
            Format = AnalysisReportFormat.CycloneDxJson,
            ReportPath = "sbom.json",
            ReportContent = """{"bomFormat":"CycloneDX","components":[]}"""
        };

        var first = await service.PublishReportAsync(runId, request, TestContext.Current.CancellationToken);
        var duplicate = await service.PublishReportAsync(runId, request, TestContext.Current.CancellationToken);

        Assert.Equal(first.Id, duplicate.Id);
        await using var verification = NewContext();
        var item = Assert.Single(await verification.DependencyTrackOutboxItems
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(first.Id, item.AnalysisReportId);
        Assert.Equal(artifactId, item.PipelineArtifactId);
        Assert.Equal(DependencyTrackOutboxStatuses.Pending, item.Status);
    }

    [Fact]
    public async Task PolicyCrudPreviewPrecedenceAndRollback_AreConsistentOnPostgres()
    {
        await ResetAndMigrateAsync();
        var (runId, _) = await SeedRunAsync();
        await using var db = NewContext();
        var run = await db.PipelineRuns.Include(item => item.Pipeline)
            .SingleAsync(item => item.Id == runId, TestContext.Current.CancellationToken);
        var projectId = run.Pipeline.ProjectId!.Value;
        var organizationId = await db.Projects.Where(item => item.Id == projectId)
            .Select(item => item.OrganizationId)
            .SingleAsync(TestContext.Current.CancellationToken);
        var service = CreateService(db);

        await service.CreateScopedPolicyAsync(null, CoverageRequest("Global coverage", 78),
            TestContext.Current.CancellationToken);
        await service.CreateScopedPolicyAsync(organizationId, CoverageRequest("Organization coverage", 82),
            TestContext.Current.CancellationToken);
        var project = await service.CreatePolicyAsync(projectId, CoverageRequest("Project coverage", 85),
            TestContext.Current.CancellationToken);

        var effective = await service.GetPoliciesAsync(projectId, TestContext.Current.CancellationToken);
        var coverage = Assert.Single(effective, item => item.PolicyKey == "quality.coverage.line");
        Assert.Equal(85, coverage.Threshold);
        Assert.Equal(AnalysisPolicyScope.Project, coverage.Scope);
        Assert.True(coverage.IsOverride);
        Assert.Equal(AnalysisPolicyScope.Organization, coverage.OverriddenScope);

        var preview = await service.PreviewPolicySetAsync(null, projectId,
            new PreviewAnalysisPolicySetRequest
            {
                PolicyId = project.Id,
                Candidate = CoverageRequest("Project coverage", 90)
            }, TestContext.Current.CancellationToken);
        Assert.Equal(90, Assert.Single(
            preview.Policies, item => item.PolicyKey == "quality.coverage.line").Threshold);
        Assert.Contains("coverage", preview.ExpectedProducers);
        Assert.Empty(preview.Conflicts);
        Assert.Equal(85, (await db.AnalysisPolicies.AsNoTracking()
            .SingleAsync(item => item.Id == project.Id, TestContext.Current.CancellationToken)).Threshold);

        await service.UpdatePolicyAsync(projectId, project.Id, CoverageRequest("Project coverage", 88),
            TestContext.Current.CancellationToken);
        var restored = await service.RollbackPolicyAsync(null, projectId, project.Id, 1,
            TestContext.Current.CancellationToken);
        var revisions = await service.GetPolicyRevisionsAsync(null, projectId, project.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, restored.Version);
        Assert.Equal(85, restored.Threshold);
        Assert.Equal([3, 2, 1], revisions.Select(item => item.Version));
    }

    [Fact]
    public async Task PolicyBatch_MidBatchConflict_RollsBackEveryRuleOnPostgres()
    {
        await ResetAndMigrateAsync();
        var (runId, _) = await SeedRunAsync();
        await using var db = NewContext();
        var projectId = await db.PipelineRuns
            .Where(run => run.Id == runId)
            .Select(run => run.Pipeline.ProjectId!.Value)
            .SingleAsync(TestContext.Current.CancellationToken);
        var service = CreateService(db);
        var request = new ApplyAnalysisPolicyBatchRequest
        {
            Items =
            [
                new AnalysisPolicyBatchItemRequest
                {
                    Policy = CoverageRequest("Batch first", 75) with
                    {
                        PolicyKey = "quality.batch.atomic"
                    }
                },
                new AnalysisPolicyBatchItemRequest
                {
                    Policy = CoverageRequest("Batch conflict", 80) with
                    {
                        PolicyKey = "quality.batch.atomic"
                    }
                }
            ]
        };

        await Assert.ThrowsAsync<Aetheus.Back.Exceptions.ConflictException>(() =>
            service.ApplyPolicyBatchAsync(
                null, projectId, request, TestContext.Current.CancellationToken));

        await using var verification = NewContext();
        Assert.False(await verification.AnalysisPolicies.AsNoTracking()
            .AnyAsync(
                policy => policy.ProjectId == projectId
                    && policy.PolicyKey == "quality.batch.atomic",
                TestContext.Current.CancellationToken));
    }

    private async Task<(int RunId, int ArtifactId)> SeedRunAsync()
    {
        await using var db = NewContext();
        var organization = new Organization { Name = $"Aetheus-{Guid.NewGuid():N}", Slug = $"aetheus-{Guid.NewGuid():N}" };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var project = new Project { Name = "Project", OrganizationId = organization.Id, DefaultBranch = "main" };
        db.Projects.Add(project);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var pipeline = new Pipeline { Name = "Security", ProjectId = project.Id, YamlDefinition = "name: security\ntrigger: manual\nstages: []" };
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var run = new PipelineRun
        {
            PipelineId = pipeline.Id,
            Status = PipelineStatus.Running,
            StartedAt = DateTime.UtcNow,
            BranchName = "feature/concurrent",
            CommitHash = new string('a', 40)
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var artifact = new PipelineArtifact
        {
            PipelineId = pipeline.Id,
            PipelineRunId = run.Id,
            Name = "analysis-opengrep",
            FilePath = "analysis-opengrep.zip"
        };
        db.PipelineArtifacts.Add(artifact);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (run.Id, artifact.Id);
    }

    private static AnalysisService CreateService(AppDbContext db, AsyncArrivalGate gate)
    {
        var repository = new AnalysisRepository(db);
        var runtime = Options.Create(new AnalysisPlatformOptions());
        return new AnalysisService(
            repository,
            new AnalysisIngestGate(runtime),
            new AnalysisPolicyEngine(repository),
            TimeProvider.System,
            Substitute.For<IAuditService>(),
            new CoordinatedTransactionScope(new DbTransactionScope(db), gate),
            Substitute.For<IEntityChangeNotifier>(),
            Substitute.For<INotificationService>(),
            Substitute.For<IDependencyTrackOutbox>(),
            runtime,
            Options.Create(new DependencyTrackOptions()));
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

    private static UpsertAnalysisPolicyRequest CoverageRequest(string name, double threshold) => new()
    {
        PolicyKey = "quality.coverage.line",
        Name = name,
        MetricKey = "coverage.line.percent",
        Operator = AnalysisPolicyOperator.LessThan,
        Threshold = threshold,
        Behavior = AnalysisGateBehavior.Block,
        Enabled = true
    };

    private static PublishAnalysisReportRequest Request(int artifactId) => new()
    {
        ScannerKey = "opengrep",
        ScannerName = "OpenGrep",
        ScannerVersion = "1.22.0",
        Category = AnalysisCategory.Sast,
        Status = AnalysisReportStatus.Failed,
        Format = AnalysisReportFormat.Sarif,
        StartedAt = DateTime.UtcNow.AddMinutes(-1),
        CompletedAt = DateTime.UtcNow,
        ReportPath = "report.sarif",
        PipelineArtifactId = artifactId,
        ReportContent = """
            {"runs":[{"tool":{"driver":{"name":"OpenGrep"}},"results":[{
              "ruleId":"security.rule","level":"error","message":{"text":"unsafe"},
              "locations":[{"physicalLocation":{"artifactLocation":{"uri":"src/App.cs"},"region":{"startLine":10}}}]
            }]}]}
            """
    };

    private static AnalysisFinding Finding(int organizationId, int projectId, string fingerprint) => new()
    {
        OrganizationId = organizationId,
        ProjectId = projectId,
        Fingerprint = fingerprint,
        RuleId = "rule",
        Category = AnalysisCategory.Sast,
        Severity = AnalysisSeverity.High,
        Title = "Finding",
        Message = "Finding",
        FirstSeenAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow
    };

    private sealed class AsyncArrivalGate(int target)
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;
        public async Task ArriveAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _arrived) == target) _allArrived.TrySetResult();
            await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
    }

    private sealed class CoordinatedTransactionScope(IDbTransactionScope inner, AsyncArrivalGate gate) : IDbTransactionScope
    {
        public bool IsRelational => inner.IsRelational;
        public async Task BeginTransactionAsync(CancellationToken ct = default)
        {
            await inner.BeginTransactionAsync(ct);
            await gate.ArriveAsync(ct);
        }
        public Task CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct = default) => inner.RollbackAsync(ct);
    }
}
