// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Back.Tests.Analysis;

/// <summary>
/// The findings and reports listings: the filters an operator applies from the analysis view
/// (category, severity, status, free text, scanner, branch, responsible, "new only"), the sort keys,
/// and the enrichment each row carries (latest occurrence, responsible taken from the linked work
/// item). The "new only" filter is the delicate one: it must scope to the latest completed report
/// per scanner and category on the branch, so a stale occurrence cannot keep a finding "new".
/// </summary>
public sealed class AnalysisRepositoryFindingsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = Now.UtcDateTime;

    private readonly FakeTimeProvider _clock = new(Now.AddDays(-30));
    private readonly AppDbContext _db;
    private readonly AnalysisRepository _repository;

    public AnalysisRepositoryFindingsTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new AppDbContext(options, _clock);
        _repository = new AnalysisRepository(_db);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SaveAsync() => await _db.SaveChangesAsync(Ct);

    /// <summary>Inserts one row as if the wall clock read <paramref name="instant"/>.</summary>
    private async Task SaveAtAsync(DateTime instant, object entity)
    {
        _clock.SetUtcNow(new DateTimeOffset(instant));
        _db.Add(entity);
        await _db.SaveChangesAsync(Ct);
    }

    private static AnalysisFinding Finding(
        int id,
        string title = "hardcoded secret",
        AnalysisCategory category = AnalysisCategory.Secrets,
        AnalysisSeverity severity = AnalysisSeverity.High,
        AnalysisFindingStatus status = AnalysisFindingStatus.Open,
        string ruleId = "rule-1",
        string? cwe = null,
        string message = "a token was committed",
        int projectId = 10,
        DateTime? lastSeenAt = null,
        DateTime? firstSeenAt = null) =>
        new()
        {
            Id = id,
            OrganizationId = 7,
            ProjectId = projectId,
            Fingerprint = $"fp-{id}",
            RuleId = ruleId,
            Category = category,
            Severity = severity,
            Status = status,
            Cwe = cwe,
            Title = title,
            Message = message,
            FirstSeenAt = firstSeenAt ?? NowUtc,
            LastSeenAt = lastSeenAt ?? NowUtc
        };

    private static AnalysisReport Report(
        int id,
        string scannerKey = "gitleaks",
        AnalysisCategory category = AnalysisCategory.Secrets,
        string? branch = "main",
        AnalysisReportStatus status = AnalysisReportStatus.Passed,
        DateTime? completedAt = null,
        int projectId = 10,
        string? commitHash = null) =>
        new()
        {
            Id = id,
            OrganizationId = 7,
            ProjectId = projectId,
            ScannerKey = scannerKey,
            ScannerName = scannerKey,
            Category = category,
            BranchName = branch,
            Status = status,
            CommitHash = commitHash,
            CompletedAt = completedAt ?? NowUtc
        };

    private static AnalysisFindingOccurrence Occurrence(
        int id, int reportId, int findingId, string scannerKey = "gitleaks",
        string toolName = "gitleaks", string? branch = "main", bool isNew = false) =>
        new()
        {
            Id = id,
            AnalysisReportId = reportId,
            AnalysisFindingId = findingId,
            ScannerKey = scannerKey,
            ToolName = toolName,
            BranchName = branch,
            IsNew = isNew
        };

    // ---------- direct filters ----------

    [Fact]
    public async Task GetFindingsAsync_FiltersOnCategorySeverityAndStatus()
    {
        _db.AnalysisFindings.AddRange(
            Finding(1, category: AnalysisCategory.Secrets, severity: AnalysisSeverity.High),
            Finding(2, category: AnalysisCategory.Sast, severity: AnalysisSeverity.High),
            Finding(3, category: AnalysisCategory.Secrets, severity: AnalysisSeverity.Low),
            Finding(4, category: AnalysisCategory.Secrets, severity: AnalysisSeverity.High,
                status: AnalysisFindingStatus.Fixed),
            Finding(5, projectId: 11));
        await SaveAsync();

        var byCategory = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { Category = AnalysisCategory.Sast }, Ct);
        var bySeverity = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { Severity = AnalysisSeverity.Low }, Ct);
        var byStatus = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { Status = AnalysisFindingStatus.Fixed }, Ct);
        var everything = await _repository.GetFindingsAsync(10, new AnalysisFindingPaginationRequest(), Ct);

        Assert.Equal([2], byCategory.Items.Select(row => row.Finding.Id));
        Assert.Equal([3], bySeverity.Items.Select(row => row.Finding.Id));
        Assert.Equal([4], byStatus.Items.Select(row => row.Finding.Id));
        Assert.Equal(4, everything.TotalCount);
    }

    [Fact]
    public async Task GetFindingsAsync_SearchesTheTitleTheMessageTheRuleAndTheCwe()
    {
        _db.AnalysisFindings.AddRange(
            Finding(1, title: "NEEDLE in the title"),
            Finding(2, message: "the needle is here"),
            Finding(3, ruleId: "needle-rule"),
            Finding(4, cwe: "CWE-NEEDLE"),
            Finding(5, title: "unrelated", message: "nothing", ruleId: "other"));
        await SaveAsync();

        var (items, total) = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { Search = "  needle  " }, Ct);

        Assert.Equal(4, total);
        Assert.Equal([1, 2, 3, 4], items.Select(row => row.Finding.Id).Order());
    }

    [Fact]
    public async Task GetFindingsAsync_FiltersOnTheScannerKeyOrItsToolName()
    {
        _db.AnalysisFindings.AddRange(Finding(1), Finding(2), Finding(3));
        _db.AnalysisReports.Add(Report(1));
        _db.AnalysisFindingOccurrences.AddRange(
            Occurrence(1, 1, 1, scannerKey: "gitleaks", toolName: "gitleaks"),
            Occurrence(2, 1, 2, scannerKey: "trivy", toolName: "trivy-fs"),
            Occurrence(3, 1, 3, scannerKey: "semgrep", toolName: "semgrep"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var byKey = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { Scanner = "GitLeaks" }, Ct);
        var byToolName = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { Scanner = "trivy-fs" }, Ct);

        Assert.Equal([1], byKey.Items.Select(row => row.Finding.Id));
        Assert.Equal([2], byToolName.Items.Select(row => row.Finding.Id));
    }

    [Fact]
    public async Task GetFindingsAsync_FiltersOnTheBranchTheOccurrenceWasSeenOn()
    {
        _db.AnalysisFindings.AddRange(Finding(1), Finding(2));
        _db.AnalysisReports.Add(Report(1));
        _db.AnalysisFindingOccurrences.AddRange(
            Occurrence(1, 1, 1, branch: "main"),
            Occurrence(2, 1, 2, branch: "release"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, _) = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { Branch = "release" }, Ct);

        Assert.Equal([2], items.Select(row => row.Finding.Id));
    }

    // ---------- "new only" ----------

    [Fact]
    public async Task GetFindingsAsync_NewOnlyScopesToTheLatestCompletedReportOfTheDefaultBranch()
    {
        _db.Projects.Add(new Project { Id = 10, Name = "aetheus", OrganizationId = 7, DefaultBranch = "main" });
        _db.AnalysisFindings.AddRange(Finding(1), Finding(2));
        _db.AnalysisReports.AddRange(
            Report(1, completedAt: NowUtc.AddDays(-1)),
            Report(2, completedAt: NowUtc));
        _db.AnalysisFindingOccurrences.AddRange(
            // Only in the superseded report: no longer "new".
            Occurrence(1, 1, 1, isNew: true),
            // In the newest report: still "new".
            Occurrence(2, 2, 2, isNew: true));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, _) = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { IsNew = true }, Ct);

        Assert.Equal([2], items.Select(row => row.Finding.Id));
    }

    [Fact]
    public async Task GetFindingsAsync_NewOnlyIgnoresAReportThatErroredOut()
    {
        _db.Projects.Add(new Project { Id = 10, Name = "aetheus", OrganizationId = 7, DefaultBranch = "main" });
        _db.AnalysisFindings.Add(Finding(1));
        _db.AnalysisReports.Add(Report(1, status: AnalysisReportStatus.Error));
        _db.AnalysisFindingOccurrences.Add(Occurrence(1, 1, 1, isNew: true));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, _) = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { IsNew = true }, Ct);

        Assert.Empty(items);
    }

    [Fact]
    public async Task GetFindingsAsync_NewOnlyHonoursAnExplicitBranchInsteadOfTheProjectDefault()
    {
        _db.AnalysisFindings.AddRange(Finding(1), Finding(2));
        _db.AnalysisReports.AddRange(
            Report(1, branch: "main"),
            Report(2, branch: "release"));
        _db.AnalysisFindingOccurrences.AddRange(
            Occurrence(1, 1, 1, branch: "main", isNew: true),
            Occurrence(2, 2, 2, branch: "release", isNew: true));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, _) = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { IsNew = true, Branch = "release" }, Ct);

        Assert.Equal([2], items.Select(row => row.Finding.Id));
    }

    [Fact]
    public async Task GetFindingsAsync_NewFalseReturnsTheKnownFindingsOfTheLatestReport()
    {
        _db.Projects.Add(new Project { Id = 10, Name = "aetheus", OrganizationId = 7, DefaultBranch = "main" });
        _db.AnalysisFindings.AddRange(Finding(1), Finding(2));
        _db.AnalysisReports.Add(Report(1));
        _db.AnalysisFindingOccurrences.AddRange(
            Occurrence(1, 1, 1, isNew: true),
            Occurrence(2, 1, 2, isNew: false));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, _) = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { IsNew = false }, Ct);

        Assert.Equal([2], items.Select(row => row.Finding.Id));
    }

    // ---------- responsible ----------

    [Fact]
    public async Task GetFindingsAsync_FiltersOnTheAssigneeOfTheLinkedWorkItem()
    {
        _db.AnalysisFindings.AddRange(Finding(1), Finding(2), Finding(3));
        _db.Users.AddRange(
            new User { Id = 1, Username = "Alice", PasswordHash = "x" },
            new User { Id = 2, Username = "bob", PasswordHash = "x" });
        _db.WorkItems.AddRange(
            WorkItem(1, findingId: 1, assigneeUserId: 1),
            WorkItem(2, findingId: 2, assigneeUserId: 2),
            WorkItem(3, findingId: 3, assigneeUserId: null));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, total) = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { Responsible = " ALI " }, Ct);

        Assert.Equal(1, total);
        Assert.Equal([1], items.Select(row => row.Finding.Id));
    }

    [Fact]
    public async Task GetFindingsAsync_ReturnsNothingWhenNobodyMatchesTheResponsibleFilter()
    {
        _db.AnalysisFindings.Add(Finding(1));
        await SaveAsync();

        var (items, _) = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { Responsible = "nobody" }, Ct);

        Assert.Empty(items);
    }

    [Fact]
    public async Task GetFindingsAsync_CarriesTheResponsibleAndTheLatestOccurrenceOnEachRow()
    {
        _db.AnalysisFindings.Add(Finding(1));
        _db.Users.Add(new User { Id = 1, Username = "alice", PasswordHash = "x" });
        _db.WorkItems.Add(WorkItem(1, findingId: 1, assigneeUserId: 1));
        _db.AnalysisReports.Add(Report(1));
        await SaveAsync();
        await SaveAtAsync(NowUtc.AddDays(-1), Occurrence(1, 1, 1, branch: "old"));
        await SaveAtAsync(NowUtc, Occurrence(2, 1, 1, branch: "latest"));
        _db.ChangeTracker.Clear();

        var row = Assert.Single((await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest(), Ct)).Items);

        Assert.Equal("alice", row.Responsible);
        Assert.Equal("latest", row.LatestOccurrence!.BranchName);
    }

    [Fact]
    public async Task GetFindingsAsync_LeavesTheEnrichmentEmptyWhenThereIsNothingToAttach()
    {
        _db.AnalysisFindings.Add(Finding(1));
        await SaveAsync();

        var row = Assert.Single((await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest(), Ct)).Items);

        Assert.Null(row.Responsible);
        Assert.Null(row.LatestOccurrence);
    }

    // ---------- ordering and paging ----------

    [Theory]
    [InlineData("Id", false, new[] { 1, 2 })]
    [InlineData("Id", true, new[] { 2, 1 })]
    [InlineData("Severity", false, new[] { 2, 1 })]
    [InlineData("Severity", true, new[] { 1, 2 })]
    [InlineData("RuleId", false, new[] { 2, 1 })]
    [InlineData("RuleId", true, new[] { 1, 2 })]
    [InlineData("FirstSeenAt", false, new[] { 1, 2 })]
    [InlineData("FirstSeenAt", true, new[] { 2, 1 })]
    [InlineData("LastSeenAt", false, new[] { 1, 2 })]
    [InlineData(null, false, new[] { 2, 1 })]
    public async Task GetFindingsAsync_SortsOnEverySupportedKey(
        string? sortBy, bool descending, int[] expected)
    {
        _db.AnalysisFindings.AddRange(
            Finding(1, severity: AnalysisSeverity.Critical, ruleId: "zulu",
                firstSeenAt: NowUtc, lastSeenAt: NowUtc),
            Finding(2, severity: AnalysisSeverity.Low, ruleId: "alpha",
                firstSeenAt: NowUtc.AddHours(1), lastSeenAt: NowUtc.AddHours(1)));
        await SaveAsync();

        var (items, _) = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { SortBy = sortBy, SortDescending = descending }, Ct);

        Assert.Equal(expected, items.Select(row => row.Finding.Id));
    }

    [Fact]
    public async Task GetFindingsAsync_PagesAfterOrderingAndKeepsTheUnpagedTotal()
    {
        _db.AnalysisFindings.AddRange(Enumerable.Range(1, 5).Select(index => Finding(index)));
        await SaveAsync();

        var (items, total) = await _repository.GetFindingsAsync(
            10, new AnalysisFindingPaginationRequest { SortBy = "Id", Page = 3, PageSize = 2 }, Ct);

        Assert.Equal(5, total);
        Assert.Equal([5], items.Select(row => row.Finding.Id));
    }

    // ---------- single finding ----------

    [Fact]
    public async Task GetFindingAsync_ReturnsNullForAnUnknownFinding()
    {
        Assert.Null(await _repository.GetFindingAsync(404, Ct));
    }

    [Fact]
    public async Task GetFindingAsync_LoadsTheNewestOccurrenceItsRunAndTheResponsible()
    {
        _db.AnalysisFindings.Add(Finding(1));
        _db.Users.Add(new User { Id = 1, Username = "alice", PasswordHash = "x" });
        _db.WorkItems.Add(WorkItem(1, findingId: 1, assigneeUserId: 1));
        _db.Pipelines.Add(new Pipeline { Id = 3, Name = "ci" });
        _db.PipelineRuns.Add(new PipelineRun { Id = 9, PipelineId = 3 });
        _db.AnalysisReports.Add(new AnalysisReport
        {
            Id = 1,
            OrganizationId = 7,
            ProjectId = 10,
            ScannerKey = "gitleaks",
            ScannerName = "gitleaks",
            Category = AnalysisCategory.Secrets,
            BranchName = "main",
            PipelineRunId = 9,
            CompletedAt = NowUtc
        });
        await SaveAsync();
        await SaveAtAsync(NowUtc.AddDays(-1), Occurrence(1, 1, 1, branch: "old"));
        await SaveAtAsync(NowUtc, Occurrence(2, 1, 1, branch: "latest"));
        _db.ChangeTracker.Clear();

        var row = await _repository.GetFindingAsync(1, Ct);

        Assert.Equal("latest", row!.LatestOccurrence!.BranchName);
        Assert.Equal("ci", row.LatestOccurrence.AnalysisReport.PipelineRun!.Pipeline.Name);
        Assert.Equal("alice", row.Responsible);
    }

    [Fact]
    public async Task GetFindingOccurrencesAsync_ReturnsTheNewestFirstAndClampsTheBatch()
    {
        _db.AnalysisFindings.Add(Finding(1));
        _db.AnalysisReports.Add(Report(1));
        await SaveAsync();
        await SaveAtAsync(NowUtc.AddDays(-2), Occurrence(1, 1, 1, branch: "oldest"));
        await SaveAtAsync(NowUtc.AddDays(-1), Occurrence(2, 1, 1, branch: "middle"));
        await SaveAtAsync(NowUtc, Occurrence(3, 1, 1, branch: "newest"));
        _db.ChangeTracker.Clear();

        var clamped = await _repository.GetFindingOccurrencesAsync(1, 0, Ct);
        var all = await _repository.GetFindingOccurrencesAsync(1, 5000, Ct);

        Assert.Equal(["newest"], clamped.Select(occurrence => occurrence.BranchName));
        Assert.Equal(["newest", "middle", "oldest"], all.Select(occurrence => occurrence.BranchName));
    }

    [Fact]
    public async Task GetFindingProjectIdAsync_ReadsTheOwnerOrNull()
    {
        _db.AnalysisFindings.Add(Finding(1, projectId: 11));
        await SaveAsync();

        Assert.Equal(11, await _repository.GetFindingProjectIdAsync(1, Ct));
        Assert.Null(await _repository.GetFindingProjectIdAsync(404, Ct));
    }

    // ---------- reports ----------

    [Fact]
    public async Task GetReportsAsync_SearchesTheScannerNameKeyAndCommit()
    {
        _db.AnalysisReports.AddRange(
            Report(1, scannerKey: "needle-scanner"),
            Report(2, scannerKey: "trivy", commitHash: "NEEDLE123"),
            Report(3, scannerKey: "semgrep"),
            Report(4, scannerKey: "needle-scanner", projectId: 11));
        await SaveAsync();

        var (items, total) = await _repository.GetReportsAsync(
            10, new PaginationRequest { Search = " needle " }, Ct);

        Assert.Equal(2, total);
        Assert.Equal([1, 2], items.Select(row => row.Report.Id).Order());
    }

    [Theory]
    [InlineData("ScannerName", false, new[] { 2, 1 })]
    [InlineData("ScannerName", true, new[] { 1, 2 })]
    [InlineData("Status", false, new[] { 1, 2 })]
    [InlineData("Status", true, new[] { 2, 1 })]
    [InlineData("CompletedAt", false, new[] { 1, 2 })]
    [InlineData(null, false, new[] { 2, 1 })]
    public async Task GetReportsAsync_SortsOnEverySupportedKey(
        string? sortBy, bool descending, int[] expected)
    {
        _db.AnalysisReports.AddRange(
            Report(1, scannerKey: "zulu", status: AnalysisReportStatus.Passed, completedAt: NowUtc),
            Report(2, scannerKey: "alpha", status: AnalysisReportStatus.Failed,
                completedAt: NowUtc.AddHours(1)));
        await SaveAsync();

        var (items, _) = await _repository.GetReportsAsync(
            10, new PaginationRequest { SortBy = sortBy, SortDescending = descending }, Ct);

        Assert.Equal(expected, items.Select(row => row.Report.Id));
    }

    [Fact]
    public async Task GetReportsAsync_CountsTheFindingsComponentsAndMetricsOfEachReport()
    {
        _db.AnalysisFindings.AddRange(Finding(1), Finding(2));
        _db.AnalysisReports.Add(Report(1));
        // Three occurrences but only two distinct findings: the count is per finding, not per row.
        _db.AnalysisFindingOccurrences.AddRange(
            Occurrence(1, 1, 1, isNew: true),
            Occurrence(2, 1, 1, isNew: false),
            Occurrence(3, 1, 2, isNew: false));
        _db.AnalysisComponents.Add(new AnalysisComponent
        {
            Id = 1, OrganizationId = 7, ProjectId = 10, AnalysisReportId = 1,
            Name = "serilog", Version = "1.0.0"
        });
        _db.AnalysisMetrics.Add(new AnalysisMetric
        {
            Id = 1, OrganizationId = 7, ProjectId = 10, AnalysisReportId = 1,
            Key = "coverage", Value = 80, ToolName = "coverlet"
        });
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var row = Assert.Single((await _repository.GetReportsAsync(10, new PaginationRequest(), Ct)).Items);

        Assert.Equal(2, row.FindingCount);
        Assert.Equal(1, row.NewFindingCount);
        Assert.Equal(1, row.ComponentCount);
        Assert.Equal(1, row.MetricCount);
    }

    [Fact]
    public async Task GetReportsAsync_PagesAfterOrdering()
    {
        _db.AnalysisReports.AddRange(Enumerable.Range(1, 5)
            .Select(index => Report(index, completedAt: NowUtc.AddMinutes(index))));
        await SaveAsync();

        var (items, total) = await _repository.GetReportsAsync(
            10, new PaginationRequest { Page = 2, PageSize = 2 }, Ct);

        Assert.Equal(5, total);
        Assert.Equal([3, 2], items.Select(row => row.Report.Id));
    }

    [Fact]
    public async Task GetProjectOrganizationIdAsync_AndOrganizationExistsAsync_AnswerFromTheStore()
    {
        _db.Organizations.Add(new Organization { Id = 7, Name = "acme", Slug = "acme" });
        _db.Projects.Add(new Project { Id = 10, Name = "aetheus", OrganizationId = 7 });
        await SaveAsync();

        Assert.Equal(7, await _repository.GetProjectOrganizationIdAsync(10, Ct));
        Assert.Null(await _repository.GetProjectOrganizationIdAsync(404, Ct));
        Assert.True(await _repository.OrganizationExistsAsync(7, Ct));
        Assert.False(await _repository.OrganizationExistsAsync(8, Ct));
    }

    private static WorkItem WorkItem(int id, int findingId, int? assigneeUserId) =>
        new()
        {
            Id = id,
            ProjectId = 10,
            Title = $"fix finding {findingId}",
            ExternalId = $"analysis:{findingId}",
            AssigneeUserId = assigneeUserId
        };

    public void Dispose() => _db.Dispose();
}
