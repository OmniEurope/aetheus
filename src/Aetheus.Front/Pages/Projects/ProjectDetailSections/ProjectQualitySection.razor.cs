// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.WebUtilities;

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

using Aetheus.Front.Pages.Analysis;

public partial class ProjectQualitySection : ComponentBase, IAsyncDisposable
{
    private static readonly string[] QualitySectionSlugs = ["overview", "findings"];

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;

    [Parameter] public int ProjectId { get; set; }

    private bool _loading = true;
    private ProjectQualityTrendDto _trend = new();
    private List<CoveragePoint> _coverage = [];
    private List<TestPoint> _tests = [];
    private List<ComplexityPoint> _complexity = [];
    private AnalysisProjectSummaryDto _summary = new();
    private List<AnalysisFindingDto> _findings = [];
    private int _findingCount;
    private string? _findingSearch;
    private AnalysisCategory? _findingCategory;
    private AnalysisSeverity? _findingSeverity;
    private AnalysisFindingStatus? _findingStatus = AnalysisFindingStatus.Open;
    private bool? _findingIsNew;
    private string? _findingScanner;
    private string? _findingBranch;
    private string? _findingResponsible;
    private Radzen.Blazor.RadzenDataGrid<AnalysisFindingDto>? _findingsGrid;
    private readonly AnalysisCategory[] _categories = Enum.GetValues<AnalysisCategory>();
    private readonly AnalysisSeverity[] _severities = Enum.GetValues<AnalysisSeverity>();
    private readonly AnalysisFindingStatus[] _findingStatuses = Enum.GetValues<AnalysisFindingStatus>();
    private AdminEntitySubscription? _realtime;
    private CancellationTokenSource? _reloadDebounce;
    private int? _loadedProjectId;
    private int _qualitySectionIndex;
    private FindingQuery? _lastFindingQueryKey;
    private string? _findingSortBy;
    private bool _findingSortDescending;
    private bool _exportingPrompt;

    private bool HasAny => _summary.Grade is not null
        || _coverage.Count > 0 || _tests.Count > 0 || _complexity.Count > 0;
    private IReadOnlyList<FindingNewnessOption> FindingNewnessOptions =>
    [
        new(true, L["AnalysisNewOnly"]),
        new(false, L["AnalysisExistingOnly"])
    ];

    private IReadOnlyList<QualityDomainView> QualityDomains
    {
        get
        {
            var source = _summary.Grade?.Domains ?? [];
            var result = new List<QualityDomainView>();
            AddDomain(result, source, AnalysisGradeDomain.Security);

            var codeAndTests = source
                .Where(domain => domain.Domain is AnalysisGradeDomain.Reliability or AnalysisGradeDomain.CodeQuality)
                .ToArray();
            if (codeAndTests.Length > 0)
            {
                result.Add(new QualityDomainView(
                    L["AnalysisCodeQualityAndTests"],
                    WorstGrade(codeAndTests),
                    codeAndTests.Any(domain => domain.Required),
                    codeAndTests.Any(domain => domain.Completeness == AnalysisGradeCompleteness.Incomplete)
                        ? AnalysisGradeCompleteness.Incomplete
                        : AnalysisGradeCompleteness.Complete,
                    codeAndTests.Where(domain => domain.EvaluatedAt.HasValue)
                        .Select(domain => domain.EvaluatedAt)
                        .Min(),
                    codeAndTests.SelectMany(domain => domain.Measures).ToArray(),
                    _complexity.LastOrDefault()?.Crap));
            }

            AddDomain(result, source, AnalysisGradeDomain.Architecture);
            AddDomain(result, source, AnalysisGradeDomain.Performance);
            return result;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        _qualitySectionIndex = QualitySectionIndexFromUri();

        // Query-only tab navigation must not replay the overview requests. The findings grid is
        // server-rendered only when its tab is active and performs its own single paged request.
        if (_loadedProjectId == ProjectId && !_loading) return;

        _loading = true;
        ResetProjectState();
        try
        {
            var trendTask = Api.Analysis.GetProjectQualityTrendAsync(ProjectId);
            var summaryTask = Api.Analysis.GetAnalysisSummaryAsync(ProjectId);
            await Task.WhenAll(trendTask, summaryTask);
            _trend = await trendTask;
            _summary = await summaryTask;
            if (_qualitySectionIndex == 1) await EnsureFindingsLoadedAsync();
        }
        catch (HttpRequestException)
        {
            ResetProjectState();
        }

        _coverage = _trend.Coverage
            .Select(c => new CoveragePoint($"#{c.RunId}", Math.Round(c.LineRate * 100, 1), Math.Round(c.BranchRate * 100, 1)))
            .ToList();
        _tests = _trend.Tests
            .Select(t => new TestPoint($"#{t.RunId}", t.Passed, t.Failed))
            .ToList();
        _complexity = _trend.Complexity
            .Where(c => c.CrapAvg.HasValue)
            .Select(c => new ComplexityPoint($"#{c.RunId}", Math.Round(c.CrapAvg!.Value, 1), Math.Round(c.AvgCyclomatic, 1)))
            .ToList();

        _loadedProjectId = ProjectId;
        _loading = false;
        if (_realtime is null)
        {
            _realtime = new AdminEntitySubscription(HubFactory);
            await _realtime.StartAsync(
                ResourceType.Project.ToString(),
                QueueRealtimeReloadAsync,
                TimeSpan.FromSeconds(60));
        }
    }

    private async Task OnQualitySectionChangedAsync(int index)
    {
        _qualitySectionIndex = Math.Clamp(index, 0, QualitySectionSlugs.Length - 1);
        if (_qualitySectionIndex == 1) await EnsureFindingsLoadedAsync();
    }

    private int QualitySectionIndexFromUri()
    {
        var query = QueryHelpers.ParseQuery(Navigation.ToAbsoluteUri(Navigation.Uri).Query);
        return query.TryGetValue("tab", out var tab)
            && string.Equals(tab.ToString(), QualitySectionSlugs[1], StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
    }

    private Task QueueRealtimeReloadAsync()
    {
        _reloadDebounce?.Cancel();
        _reloadDebounce?.Dispose();
        _reloadDebounce = new CancellationTokenSource();
        var token = _reloadDebounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
                await InvokeAsync(async () =>
                {
                    _summary = await Api.Analysis.GetAnalysisSummaryAsync(ProjectId, token);
                    if (_qualitySectionIndex == 1 && _findingsGrid is not null)
                    {
                        _lastFindingQueryKey = null;
                        await _findingsGrid.Reload();
                    }
                    StateHasChanged();
                });
            }
            catch (OperationCanceledException) { }
        }, token);
        return Task.CompletedTask;
    }

    private async Task LoadFindingsAsync(LoadDataArgs args)
    {
        var queryKey = FindingQueryKey(args);
        if (queryKey == _lastFindingQueryKey) return;

        var (page, size) = Page(args);
        var result = await Api.Analysis.GetAnalysisFindingsAsync(ProjectId, page, size, _findingSearch,
            _findingCategory, _findingSeverity, _findingStatus, _findingIsNew, _findingScanner,
            _findingBranch, _findingResponsible, sortBy: Sort(args), sortDescending: Descending(args));
        _findings = result.Items;
        _findingCount = result.TotalCount;
        _findingSortBy = Sort(args);
        _findingSortDescending = Descending(args);
        _lastFindingQueryKey = queryKey;
    }

    private Task EnsureFindingsLoadedAsync() => _lastFindingQueryKey is null
        ? LoadFindingsAsync(new LoadDataArgs { Skip = 0, Top = 25 })
        : Task.CompletedTask;

    private void OpenFinding(DataGridRowMouseEventArgs<AnalysisFindingDto> args)
    {
        if (args.Data is { } finding) Navigation.NavigateTo($"/analysis/findings/{finding.Id}");
    }

    private async Task OpenFindingsAsync(
        AnalysisFindingStatus? status = null,
        AnalysisSeverity? severity = null,
        bool? isNew = null)
    {
        _findingSearch = null;
        _findingCategory = null;
        _findingSeverity = severity;
        _findingStatus = status;
        _findingIsNew = isNew;
        _findingScanner = null;
        _findingBranch = null;
        _findingResponsible = null;
        _lastFindingQueryKey = null;
        SelectQualitySection(1);
        await EnsureFindingsLoadedAsync();
    }

    private void SelectQualitySection(int index)
    {
        _qualitySectionIndex = Math.Clamp(index, 0, QualitySectionSlugs.Length - 1);
        var slug = _qualitySectionIndex == 0 ? null : QualitySectionSlugs[_qualitySectionIndex];
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("tab", slug));
    }

    private static string SectionText(string label, int count) =>
        count > 0 ? $"{label} · {count:N0}" : label;

    private async Task ReloadFindingsAsync(object _)
    {
        _lastFindingQueryKey = null;
        await ReloadFromFirstPageAsync(_findingsGrid);
    }

    private async Task ExportAllAiPromptAsync()
    {
        if (_exportingPrompt || _findingCount == 0) return;

        _exportingPrompt = true;
        try
        {
            const int pageSize = 200;
            var page = 1;
            var findings = new List<AnalysisFindingDto>(_findingCount);
            while (findings.Count < _findingCount)
            {
                var result = await Api.Analysis.GetAnalysisFindingsAsync(
                    ProjectId,
                    page,
                    pageSize,
                    _findingSearch,
                    _findingCategory,
                    _findingSeverity,
                    _findingStatus,
                    _findingIsNew,
                    _findingScanner,
                    _findingBranch,
                    _findingResponsible,
                    _findingSortBy,
                    _findingSortDescending);
                findings.AddRange(result.Items);
                if (result.Items.Count == 0 || findings.Count >= result.TotalCount) break;
                page++;
            }

            var markdown = AnalysisAiPromptBuilder.BuildBulk(findings, L);
            await Js.InvokeVoidAsync(
                "downloadFile",
                $"project-{ProjectId}-findings-ai-prompt.md",
                markdown,
                "text/markdown");
        }
        finally
        {
            _exportingPrompt = false;
        }
    }

    private static async Task ReloadFromFirstPageAsync<T>(Radzen.Blazor.RadzenDataGrid<T>? grid)
        where T : notnull
    {
        if (grid is null) return;
        await grid.GoToPage(0);
        await grid.Reload();
    }

    private void ResetProjectState()
    {
        _trend = new();
        _summary = new();
        _findings = [];
        _findingCount = 0;
        _findingStatus = AnalysisFindingStatus.Open;
        _findingSortBy = null;
        _findingSortDescending = false;
        _exportingPrompt = false;
        _lastFindingQueryKey = null;
    }

    private static (int Page, int Size) Page(LoadDataArgs args)
    {
        var size = Math.Clamp(args.Top ?? 25, 1, 200);
        return (((args.Skip ?? 0) / size) + 1, size);
    }

    private static string? Sort(LoadDataArgs args) => args.Sorts?.FirstOrDefault()?.Property;
    private static bool Descending(LoadDataArgs args) =>
        args.Sorts?.FirstOrDefault()?.SortOrder == SortOrder.Descending;

    private FindingQuery FindingQueryKey(LoadDataArgs args)
    {
        var (page, size) = Page(args);
        return new FindingQuery(
            page,
            size,
            _findingSearch,
            _findingCategory,
            _findingSeverity,
            _findingStatus,
            _findingIsNew,
            _findingScanner,
            _findingBranch,
            _findingResponsible,
            Sort(args),
            Descending(args));
    }

    private static BadgeStyle SeverityStyle(AnalysisSeverity severity) =>
        AnalysisPresentation.SeverityBadge(severity);

    private string DomainLabel(AnalysisGradeDomain? domain) => domain switch
    {
        AnalysisGradeDomain.Security => L["AnalysisApplicationSecurity"],
        AnalysisGradeDomain.Reliability or AnalysisGradeDomain.CodeQuality => L["AnalysisCodeQualityAndTests"],
        AnalysisGradeDomain.Architecture => L["AnalysisCodeArchitecture"],
        AnalysisGradeDomain.Performance => L["AnalysisRuntimePerformance"],
        _ => L["NotAvailable"]
    };

    private string MeasureLabel(string key) => key switch
    {
        "grade.security.critical" => L["AnalysisCriticalVulnerabilities"],
        "grade.security.high" => L["AnalysisHighVulnerabilities"],
        "grade.security.medium" => L["AnalysisMediumVulnerabilities"],
        "grade.coverage.line" => L["AnalysisTestedLines"],
        "grade.coverage.branch" => L["AnalysisTestedBranches"],
        "grade.complexity.maximum" => L["AnalysisMaximumComplexity"],
        "grade.duplication" => L["AnalysisDuplicatedCode"],
        "grade.architecture.cycles" => L["AnalysisDependencyCycles"],
        _ => key.StartsWith("grade.", StringComparison.OrdinalIgnoreCase)
            ? key["grade.".Length..].Replace('.', ' ')
            : key
    };

    private static string GradeText(AnalysisGrade? grade) => grade?.ToString() ?? "-";

    private static string GradeCss(AnalysisGrade? grade) => AnalysisPresentation.GradeCss(grade);

    private static string ShortCommit(string? commit) => string.IsNullOrWhiteSpace(commit)
        ? "-"
        : commit[..Math.Min(12, commit.Length)];

    private static string MeasureText(AnalysisGradeMeasureDto measure)
    {
        if (!measure.ObservedValue.HasValue) return "-";
        var value = measure.ObservedValue.Value.ToString("0.##");
        if (string.Equals(measure.Unit, "percent", StringComparison.OrdinalIgnoreCase)) return $"{value} %";
        return string.IsNullOrWhiteSpace(measure.Unit) ? value : $"{value} {measure.Unit}";
    }

    private void AddDomain(
        ICollection<QualityDomainView> target,
        IReadOnlyList<AnalysisGradeDomainDto> source,
        AnalysisGradeDomain domain)
    {
        var item = source.FirstOrDefault(candidate => candidate.Domain == domain);
        if (item is null) return;
        target.Add(new QualityDomainView(
            DomainLabel(domain), item.Grade, item.Required, item.Completeness, item.EvaluatedAt, item.Measures, null));
    }

    private static AnalysisGrade? WorstGrade(IEnumerable<AnalysisGradeDomainDto> domains) =>
        domains.Where(domain => domain.Grade.HasValue)
            .Select(domain => domain.Grade)
            .Max();

    private sealed record CoveragePoint(string Label, double LinePct, double BranchPct);
    private sealed record TestPoint(string Label, int Passed, int Failed);
    private sealed record ComplexityPoint(string Label, double Crap, double AvgCc);
    private sealed record FindingNewnessOption(bool Value, string Label);
    private sealed record QualityDomainView(
        string Label,
        AnalysisGrade? Grade,
        bool Required,
        AnalysisGradeCompleteness Completeness,
        DateTime? EvaluatedAt,
        IReadOnlyList<AnalysisGradeMeasureDto> Measures,
        double? CrapScore);

    public async ValueTask DisposeAsync()
    {
        _reloadDebounce?.Cancel();
        _reloadDebounce?.Dispose();
        if (_realtime is not null) await _realtime.DisposeAsync();
    }
}
