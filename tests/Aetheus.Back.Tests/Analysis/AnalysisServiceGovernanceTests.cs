// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.Analysis;

/// <summary>
/// The governance and read surface of the analysis service: policy-exception scoping, manual finding
/// decisions, the portfolio period check, and the paged read wrappers. The publishing pipeline is
/// covered by <see cref="AnalysisServiceTests"/>; this suite is about what an operator can and
/// cannot record by hand, because those rules are what suppress a finding from the gate.
/// </summary>
public sealed class AnalysisServiceGovernanceTests : IDisposable
{
    private static readonly DateTime NowUtc = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(NowUtc));
    private readonly AppDbContext _db;
    private readonly AnalysisService _service;
    private readonly AnalysisFindingDecisionService _decisions;
    private readonly IAuditService _audit = Substitute.For<IAuditService>();

    public AnalysisServiceGovernanceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new AppDbContext(options, _clock);
        _db.Organizations.Add(new Organization { Id = 7, Name = "acme", Slug = "acme" });
        _db.Projects.AddRange(
            new Project { Id = 10, Name = "aetheus", OrganizationId = 7, DefaultBranch = "main" },
            new Project { Id = 11, Name = "other", OrganizationId = 8, DefaultBranch = "main" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var repository = new AnalysisRepository(_db);
        _decisions = new AnalysisFindingDecisionService(repository, _clock, _audit);
        _service = new AnalysisService(
            repository,
            new AnalysisIngestGate(Options.Create(new AnalysisPlatformOptions())),
            new AnalysisPolicyEngine(repository),
            _clock,
            Substitute.For<IAuditService>(),
            Substitute.For<IDbTransactionScope>(),
            Substitute.For<IEntityChangeNotifier>(),
            Substitute.For<INotificationService>(),
            Substitute.For<IDependencyTrackOutbox>(),
            Options.Create(new AnalysisPlatformOptions()),
            Options.Create(new DependencyTrackOptions()));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SaveAsync() => await _db.SaveChangesAsync(Ct);

    private static CreateAnalysisPolicyExceptionRequest ExceptionRequest(
        int? findingId = null,
        int? policyId = null,
        string? fingerprint = null,
        string? ruleId = null,
        string? scannerKey = null,
        AnalysisCategory? category = null,
        DateTime? expiresAt = null) =>
        new()
        {
            AnalysisFindingId = findingId,
            AnalysisPolicyId = policyId,
            Fingerprint = fingerprint,
            RuleId = ruleId,
            ScannerKey = scannerKey,
            Category = category,
            Reason = "accepted for the current release train",
            ExpiresAt = expiresAt ?? NowUtc.AddDays(30)
        };

    private AnalysisFinding AddFinding(int id = 1, int projectId = 10)
    {
        var finding = new AnalysisFinding
        {
            Id = id,
            OrganizationId = 7,
            ProjectId = projectId,
            Fingerprint = $"fp-{id}",
            RuleId = "rule-1",
            Category = AnalysisCategory.Secrets,
            Severity = AnalysisSeverity.High,
            Status = AnalysisFindingStatus.Open,
            Title = "hardcoded secret",
            Message = "a token was committed"
        };
        _db.AnalysisFindings.Add(finding);
        return finding;
    }

    // ---------- exception scoping ----------

    [Fact]
    public async Task CreateExceptionAsync_RefusesAnUnknownProject()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateExceptionAsync(404, ExceptionRequest(ruleId: "rule-1"), "alice", Ct));
    }

    [Fact]
    public async Task CreateExceptionAsync_RefusesAnExpirationThatIsNotInTheFuture()
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateExceptionAsync(
                10, ExceptionRequest(ruleId: "rule-1", expiresAt: NowUtc), "alice", Ct));
    }

    [Fact]
    public async Task CreateExceptionAsync_RefusesAnExceptionWithNoScopeAtAll()
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateExceptionAsync(10, ExceptionRequest(), "alice", Ct));
    }

    [Fact]
    public async Task CreateExceptionAsync_RefusesAFindingOwnedByAnotherProject()
    {
        AddFinding(1, projectId: 11);
        await SaveAsync();
        _db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateExceptionAsync(10, ExceptionRequest(findingId: 1), "alice", Ct));
    }

    [Fact]
    public async Task CreateExceptionAsync_RefusesAnUnknownFinding()
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateExceptionAsync(10, ExceptionRequest(findingId: 404), "alice", Ct));
    }

    [Fact]
    public async Task CreateExceptionAsync_RefusesAPolicyBelongingToAnotherScope()
    {
        _db.AnalysisPolicies.Add(new AnalysisPolicy { Id = 1, OrganizationId = 8, Name = "other-org" });
        await SaveAsync();
        _db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateExceptionAsync(
                10, ExceptionRequest(ruleId: "rule-1", policyId: 1), "alice", Ct));
        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateExceptionAsync(
                10, ExceptionRequest(ruleId: "rule-1", policyId: 404), "alice", Ct));
    }

    [Fact]
    public async Task CreateExceptionAsync_RecordsAScopedExceptionWithItsOwnerAndTrimmedFields()
    {
        var created = await _service.CreateExceptionAsync(
            10,
            ExceptionRequest(ruleId: "  rule-1  ", scannerKey: "  gitleaks  ", fingerprint: "   "),
            "alice",
            Ct);

        Assert.Equal("rule-1", created.RuleId);
        Assert.Equal("gitleaks", created.ScannerKey);
        Assert.Null(created.Fingerprint);
        Assert.Equal("alice", created.CreatedByUsername);
        var stored = Assert.Single(await _db.AnalysisPolicyExceptions.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(7, stored.OrganizationId);
        Assert.Equal(10, stored.ProjectId);
    }

    [Fact]
    public async Task CreateExceptionAsync_AcceptsAPolicyThatAppliesToThisProject()
    {
        _db.AnalysisPolicies.Add(new AnalysisPolicy { Id = 1, OrganizationId = 7, ProjectId = 10, Name = "ours" });
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var created = await _service.CreateExceptionAsync(
            10, ExceptionRequest(category: AnalysisCategory.Secrets, policyId: 1), "alice", Ct);

        Assert.Equal(1, created.AnalysisPolicyId);
    }

    [Fact]
    public async Task GetExceptionsAsync_ListsWhatWasRecordedForThatProject()
    {
        await _service.CreateExceptionAsync(10, ExceptionRequest(ruleId: "rule-1"), "alice", Ct);
        _db.ChangeTracker.Clear();

        var exceptions = await _service.GetExceptionsAsync(10, Ct);

        Assert.Equal(["rule-1"], exceptions.Select(item => item.RuleId));
        Assert.Empty(await _service.GetExceptionsAsync(11, Ct));
    }

    [Fact]
    public async Task RevokeExceptionAsync_StampsTheRevocationOnceAndIsIdempotent()
    {
        var created = await _service.CreateExceptionAsync(10, ExceptionRequest(ruleId: "rule-1"), "alice", Ct);
        _db.ChangeTracker.Clear();

        await _service.RevokeExceptionAsync(10, created.Id, "alice", Ct);
        _db.ChangeTracker.Clear();
        var revokedAt = (await _db.AnalysisPolicyExceptions.AsNoTracking().FirstAsync(Ct)).RevokedAt;

        _clock.Advance(TimeSpan.FromHours(1));
        await _service.RevokeExceptionAsync(10, created.Id, "alice", Ct);
        _db.ChangeTracker.Clear();

        Assert.Equal(NowUtc, revokedAt);
        Assert.Equal(revokedAt, (await _db.AnalysisPolicyExceptions.AsNoTracking().FirstAsync(Ct)).RevokedAt);
    }

    [Fact]
    public async Task RevokeExceptionAsync_RefusesAnUnknownExceptionOrTheWrongProject()
    {
        var created = await _service.CreateExceptionAsync(10, ExceptionRequest(ruleId: "rule-1"), "alice", Ct);
        _db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.RevokeExceptionAsync(10, 404, "alice", Ct));
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.RevokeExceptionAsync(11, created.Id, "alice", Ct));
    }

    // ---------- manual finding decisions ----------

    [Theory]
    [InlineData(AnalysisFindingStatus.Open)]
    [InlineData(AnalysisFindingStatus.Fixed)]
    public async Task CreateFindingDecisionAsync_RefusesAStatusThatIsNotAManualDecision(
        AnalysisFindingStatus status)
    {
        AddFinding();
        await SaveAsync();
        _db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<BadRequestException>(
            () => _decisions.CreateFindingDecisionAsync(
                1,
                new CreateAnalysisFindingDecisionRequest { Status = status, Reason = "not allowed here" },
                "alice",
                Ct));
    }

    [Fact]
    public async Task CreateFindingDecisionAsync_RefusesAnExpirationThatIsNotInTheFuture()
    {
        AddFinding();
        await SaveAsync();
        _db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<BadRequestException>(
            () => _decisions.CreateFindingDecisionAsync(
                1,
                new CreateAnalysisFindingDecisionRequest
                {
                    Status = AnalysisFindingStatus.Accepted,
                    Reason = "accepted for this release",
                    ExpiresAt = NowUtc
                },
                "alice",
                Ct));
    }

    [Fact]
    public async Task CreateFindingDecisionAsync_RefusesAnUnknownFinding()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _decisions.CreateFindingDecisionAsync(
                404,
                new CreateAnalysisFindingDecisionRequest
                {
                    Status = AnalysisFindingStatus.Accepted,
                    Reason = "accepted for this release"
                },
                "alice",
                Ct));
    }

    [Fact]
    public async Task CreateFindingDecisionAsync_MovesTheFindingAndRecordsWhoDecidedIt()
    {
        AddFinding();
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var decision = await _decisions.CreateFindingDecisionAsync(
            1,
            new CreateAnalysisFindingDecisionRequest
            {
                Status = AnalysisFindingStatus.FalsePositive,
                Reason = "  the scanner matched a test fixture  ",
                ExpiresAt = NowUtc.AddDays(30)
            },
            "alice",
            Ct);
        _db.ChangeTracker.Clear();

        Assert.Equal(AnalysisFindingStatus.FalsePositive, decision.Status);
        Assert.Equal("the scanner matched a test fixture", decision.Reason);
        Assert.Equal("alice", decision.CreatedByUsername);
        var finding = await _db.AnalysisFindings.AsNoTracking().FirstAsync(Ct);
        Assert.Equal(AnalysisFindingStatus.FalsePositive, finding.Status);
        Assert.Null(finding.ResolvedAt);
        Assert.Equal(NowUtc, finding.UpdatedAt);
    }

    [Fact]
    public async Task CreateFindingDecisionAsync_RevokesThePreviousActiveDecision()
    {
        AddFinding();
        await SaveAsync();
        _db.ChangeTracker.Clear();
        await _decisions.CreateFindingDecisionAsync(
            1,
            new CreateAnalysisFindingDecisionRequest
            {
                Status = AnalysisFindingStatus.Accepted,
                Reason = "accepted for this release"
            },
            "alice",
            Ct);
        _db.ChangeTracker.Clear();
        _clock.Advance(TimeSpan.FromHours(1));

        await _decisions.CreateFindingDecisionAsync(
            1,
            new CreateAnalysisFindingDecisionRequest
            {
                Status = AnalysisFindingStatus.Mitigated,
                Reason = "mitigated by the new gateway rule"
            },
            "alice",
            Ct);
        _db.ChangeTracker.Clear();

        var decisions = await _decisions.GetFindingDecisionsAsync(1, Ct);
        Assert.Equal(2, decisions.Count);
        Assert.Single(decisions, decision => decision.RevokedAt is null);
        Assert.Equal(
            AnalysisFindingStatus.Mitigated,
            decisions.Single(decision => decision.RevokedAt is null).Status);
    }

    private async Task DecideAsync(AnalysisFindingStatus status = AnalysisFindingStatus.Accepted)
    {
        await _decisions.CreateFindingDecisionAsync(
            1,
            new CreateAnalysisFindingDecisionRequest { Status = status, Reason = "accepted for this release" },
            "alice",
            Ct);
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task R2_027_RevokeActiveFindingDecisionAsync_ReopensTheFinding_KeepsTheHistory_AndAudits()
    {
        AddFinding();
        await SaveAsync();
        _db.ChangeTracker.Clear();
        await DecideAsync(AnalysisFindingStatus.FalsePositive);
        _clock.Advance(TimeSpan.FromHours(2));

        await _decisions.RevokeActiveFindingDecisionAsync(1, "bob", Ct);
        _db.ChangeTracker.Clear();

        var finding = await _db.AnalysisFindings.AsNoTracking().FirstAsync(Ct);
        Assert.Equal(AnalysisFindingStatus.Open, finding.Status);
        Assert.Equal(NowUtc.AddHours(2), finding.UpdatedAt);
        var decision = Assert.Single(await _decisions.GetFindingDecisionsAsync(1, Ct));
        Assert.Equal(AnalysisFindingStatus.FalsePositive, decision.Status);
        Assert.Equal(NowUtc.AddHours(2), decision.RevokedAt);
        await _audit.Received(1).LogAsync("AnalysisFindingDecisionRevoked", nameof(AnalysisFinding), 1,
            Arg.Is<string>(details => details.Contains("actor=bob", StringComparison.Ordinal)
                && details.Contains("status=Open", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task R2_027_RevokeActiveFindingDecisionAsync_LeavesAFixedFindingFixed()
    {
        AddFinding();
        await SaveAsync();
        _db.ChangeTracker.Clear();
        await DecideAsync();
        var tracked = await _db.AnalysisFindings.FirstAsync(Ct);
        tracked.Status = AnalysisFindingStatus.Fixed;
        await SaveAsync();
        _db.ChangeTracker.Clear();

        await _decisions.RevokeActiveFindingDecisionAsync(1, "bob", Ct);
        _db.ChangeTracker.Clear();

        Assert.Equal(AnalysisFindingStatus.Fixed, (await _db.AnalysisFindings.AsNoTracking().FirstAsync(Ct)).Status);
        Assert.NotNull(Assert.Single(await _decisions.GetFindingDecisionsAsync(1, Ct)).RevokedAt);
    }

    [Fact]
    public async Task R2_027_RevokeActiveFindingDecisionAsync_RefusesAFindingWithoutActiveDecision_OrUnknown()
    {
        AddFinding();
        await SaveAsync();
        _db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<ConflictException>(() => _decisions.RevokeActiveFindingDecisionAsync(1, "bob", Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => _decisions.RevokeActiveFindingDecisionAsync(404, "bob", Ct));
        await DecideAsync();
        await _decisions.RevokeActiveFindingDecisionAsync(1, "bob", Ct);
        _db.ChangeTracker.Clear();
        // Reverted once: nothing is left to revert.
        await Assert.ThrowsAsync<ConflictException>(() => _decisions.RevokeActiveFindingDecisionAsync(1, "bob", Ct));
    }

    [Fact]
    public async Task GetFindingDecisionsAsync_IsEmptyForAFindingNobodyDecidedOn()
    {
        Assert.Empty(await _decisions.GetFindingDecisionsAsync(404, Ct));
    }

    // ---------- read wrappers ----------

    [Fact]
    public async Task GetPortfolioAsync_RefusesAPeriodThatEndsBeforeItStarts()
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.GetPortfolioAsync(
                null,
                new AnalysisPortfolioPaginationRequest { From = NowUtc, To = NowUtc.AddDays(-1) },
                Ct));
    }

    [Fact]
    public async Task GetPortfolioAsync_ReportsTheNormalizedPageOfAnEmptyPortfolio()
    {
        var page = await _service.GetPortfolioAsync(
            [10], new AnalysisPortfolioPaginationRequest { Page = 2, PageSize = 10 }, Ct);

        Assert.Empty(page.Items);
        Assert.Equal(2, page.Page);
        Assert.Equal(10, page.PageSize);
    }

    [Fact]
    public async Task GetMetricsAsync_AndGetComponentsAsync_MapTheRepositoryPage()
    {
        _db.AnalysisReports.Add(new AnalysisReport
        {
            Id = 1,
            OrganizationId = 7,
            ProjectId = 10,
            ScannerKey = "coverlet",
            ScannerName = "coverlet",
            Category = AnalysisCategory.Coverage,
            BranchName = "main",
            CompletedAt = NowUtc
        });
        _db.AnalysisMetrics.Add(new AnalysisMetric
        {
            Id = 1,
            OrganizationId = 7,
            ProjectId = 10,
            AnalysisReportId = 1,
            Key = "coverage",
            Value = 80,
            ToolName = "coverlet"
        });
        _db.AnalysisComponents.Add(new AnalysisComponent
        {
            Id = 1,
            OrganizationId = 7,
            ProjectId = 10,
            AnalysisReportId = 1,
            Name = "serilog",
            Version = "1.0.0"
        });
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var metrics = await _service.GetMetricsAsync(10, new AnalysisMetricPaginationRequest(), Ct);
        var components = await _service.GetComponentsAsync(10, new AnalysisComponentPaginationRequest(), Ct);

        Assert.Equal("coverage", Assert.Single(metrics.Items).Key);
        Assert.Equal(1, metrics.TotalCount);
        Assert.Equal("serilog", Assert.Single(components.Items).Name);
        Assert.Equal(1, components.TotalCount);
    }

    [Fact]
    public async Task GetTrackingStatusAsync_ReportsNothingUntilTheProjectIsTracked()
    {
        Assert.Null(await _service.GetTrackingStatusAsync(10, Ct));

        _db.AnalysisTrackingProjects.Add(new AnalysisTrackingProject
        {
            Id = 1,
            OrganizationId = 7,
            ProjectId = 10,
            ExternalProjectId = "ext-1",
            SyncStatus = "Error",
            LastError = "401 from Dependency-Track",
            LastKnownVulnerabilityCount = 3,
            LastSyncAt = NowUtc
        });
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var status = await _service.GetTrackingStatusAsync(10, Ct);

        Assert.Equal("Error", status!.SyncStatus);
        Assert.Equal("401 from Dependency-Track", status.LastError);
        Assert.Equal(3, status.LastKnownVulnerabilityCount);
        Assert.Equal(NowUtc, status.LastSyncAt);
    }

    [Fact]
    public async Task GetVulnerabilityObservationsAsync_MapsEveryFieldOfTheObservation()
    {
        _db.AnalysisVulnerabilityObservations.Add(new AnalysisVulnerabilityObservation
        {
            Id = 1,
            OrganizationId = 7,
            ProjectId = 10,
            AnalysisTrackingProjectId = 1,
            VulnerabilityId = "CVE-2026-1",
            ComponentName = "serilog",
            ComponentVersion = "1.0.0",
            PackageUrl = "pkg:nuget/Serilog@1.0.0",
            Severity = AnalysisSeverity.Critical,
            Status = "Open",
            IsContinuous = true,
            ObservedAt = NowUtc
        });
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var page = await _service.GetVulnerabilityObservationsAsync(10, new PaginationRequest(), Ct);

        var observation = Assert.Single(page.Items);
        Assert.Equal("CVE-2026-1", observation.VulnerabilityId);
        Assert.Equal("serilog", observation.ComponentName);
        Assert.Equal("pkg:nuget/Serilog@1.0.0", observation.PackageUrl);
        Assert.Equal(AnalysisSeverity.Critical, observation.Severity);
        Assert.True(observation.IsContinuous);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task GetFindingAsync_AndItsProjectAndOccurrences_AnswerForAnUnknownFinding()
    {
        Assert.Null(await _service.GetFindingAsync(404, Ct));
        Assert.Null(await _service.GetFindingProjectIdAsync(404, Ct));
        Assert.Empty(await _service.GetFindingOccurrencesAsync(404, 10, Ct));
    }

    [Fact]
    public async Task GetFindingAsync_MapsAKnownFindingAndItsOwner()
    {
        AddFinding();
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var finding = await _service.GetFindingAsync(1, Ct);

        Assert.Equal("hardcoded secret", finding!.Title);
        Assert.Equal(10, await _service.GetFindingProjectIdAsync(1, Ct));
    }

    [Fact]
    public async Task GetScopedPoliciesAsync_RefusesAnUnknownOrganization()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetScopedPoliciesAsync(404, Ct));
    }

    [Fact]
    public async Task GetPoliciesAsync_RefusesAnUnknownProject()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetPoliciesAsync(404, Ct));
    }

    public void Dispose() => _db.Dispose();
}
