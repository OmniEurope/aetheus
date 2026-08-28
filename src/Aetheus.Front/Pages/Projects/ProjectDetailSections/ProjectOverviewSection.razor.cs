// SPDX-License-Identifier: EUPL-1.2
using Markdig;

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectOverviewSection : IAsyncDisposable
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;

    [Parameter, EditorRequired] public ProjectDetailDto? Project { get; set; }

    private const int RecentItemLimit = 5;
    private List<PipelineRunDto> _recentPipelineRuns = [];
    private List<RecentCommitView> _recentCommits = [];
    private bool _loadingRecentPipelines = true;
    private bool _loadingRecentCommits = true;
    private bool _gitStateLoaded;
    private bool _hasGitRepository;
    private GitLightRepoDto? _primaryGitRepository;
    private HubConnection? _pipelineHub;
    private readonly TrailingReloadCoalescer _pipelineReload = new(500);

    // F-ter: README (rendered from the project's git repo, same Markdig pipeline as GitRepositoryDetail)
    // and the release changelog timeline. Their tabs stay visible and show an honest empty state.
    private string? _readmeHtml;
    private string? _readmeBranch;
    private List<ReleaseDto> _changelog = [];
    private int? _loadedProjectId;

    private sealed record RecentCommitView(
        int RepositoryId,
        string RepositoryName,
        string Sha,
        string ShortSha,
        string BranchName,
        string Message,
        string AuthorName,
        DateTime AuthorDate)
    {
        public static RecentCommitView FromCommit(GitLightRepoDto repository, GitLightCommitDto commit)
        {
            var branchRefs = commit.RefNames
                .Where(reference => !reference.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
                .Select(reference => reference.StartsWith("origin/", StringComparison.OrdinalIgnoreCase)
                    ? reference["origin/".Length..]
                    : reference)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new(
                repository.Id,
                repository.Name,
                commit.Sha,
                commit.ShortSha,
                branchRefs.Count == 0 ? "-" : string.Join(", ", branchRefs),
                commit.Message,
                commit.AuthorName,
                commit.AuthorDate);
        }
    }

    private static readonly string[] TabSlugs = ["overview", "readme", "changelog"];

    // F-bis: "Métriques · dernière release" - coverage (CoverageSummary) and LOC (RunMetric "loc.total")
    // of the run that produced the project's most recent release. Null when the latest release has no
    // linked run, or that run carries neither metric - never a fabricated value.
    private LastReleaseMetricsView? _lastReleaseMetrics;

    private sealed record LastReleaseMetricsView(
        string Version, DateTime When, double? CoverageLineRate, int? LinesOfCode);

    // Coverage threshold colouring, consistent with the run view (>=80% green, >=50% amber, else red).
    private static string CoverageColorClass(double rate) => rate switch
    {
        >= 0.8 => "rz-color-success",
        >= 0.5 => "rz-color-warning",
        _ => "rz-color-danger"
    };

    private static string GradeText(AnalysisGrade? grade) => grade?.ToString() ?? "-";

    private static string GradeCss(AnalysisGrade? grade) =>
        $"analysis-grade-{grade?.ToString().ToLowerInvariant() ?? "na"}";

    // Mirrors GitRepositoryDetail: HTML disabled so untrusted README markup can't inject script.
    private static readonly MarkdownPipeline _markdownPipeline =
        new MarkdownPipelineBuilder().DisableHtml().Build();

    /// <summary>F: last-run status across the project's pipelines (most recent run), for the tile dot.</summary>
    private PipelineStatus? LatestRunStatus =>
        _recentPipelineRuns.FirstOrDefault()?.Status
        ?? Project?.Pipelines
            .Where(p => p.LastRunAt.HasValue && p.LastRunStatus.HasValue)
            .OrderByDescending(p => p.LastRunAt)
            .Select(p => p.LastRunStatus)
            .FirstOrDefault();

    private bool ShowGettingStarted => Project is not null && _gitStateLoaded
        && (!_hasGitRepository || Project.EnvironmentCount == 0 || Project.Pipelines.Count == 0);

    private string GitOnboardingHref => _hasGitRepository
        ? $"/git-repositories?projectId={Project!.Id}"
        : $"/git-repositories?projectId={Project!.Id}&create=true";

    private string EnvironmentOnboardingHref => Project!.EnvironmentCount > 0
        ? $"/projects/{Project.Id}/environments"
        : $"/environments/new?projectId={Project.Id}";

    private string PipelineOnboardingHref => Project!.Pipelines.Count > 0
        ? $"/projects/{Project.Id}/pipelines"
        : $"/pipelines/setup?projectId={Project.Id}";

    private void AddGitRepository()
    {
        if (Project is not null)
            Nav.NavigateTo($"/git-repositories?projectId={Project.Id}&create=true");
    }

    private string? RepositoryDisplayUrl => !string.IsNullOrWhiteSpace(Project?.RepositoryUrl)
        ? Project.RepositoryUrl
        : _primaryGitRepository?.CloneUrl;

    private string? RepositoryDisplayBranch => !string.IsNullOrWhiteSpace(Project?.DefaultBranch)
        ? Project.DefaultBranch
        : _primaryGitRepository?.DefaultBranch;

    protected override async Task OnInitializedAsync()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        await StartPipelineHubAsync();
    }

    private void OnPermissionsChanged() => InvokeAsync(StateHasChanged);

    /// <summary>Maps a release status to a timeline point color for the changelog.</summary>
    private static PointStyle ReleasePointStyle(ReleaseStatus status) => status switch
    {
        ReleaseStatus.Published or ReleaseStatus.Promoted or ReleaseStatus.Superseded => PointStyle.Success,
        ReleaseStatus.Failed => PointStyle.Danger,
        ReleaseStatus.Building => PointStyle.Info,
        ReleaseStatus.RolledBack => PointStyle.Warning,
        _ => PointStyle.Base
    };

    protected override async Task OnParametersSetAsync()
    {
        if (Project is null || _loadedProjectId == Project.Id) return;
        _loadedProjectId = Project.Id;
        _recentPipelineRuns = [];
        _recentCommits = [];
        _loadingRecentPipelines = true;
        _loadingRecentCommits = true;
        _gitStateLoaded = false;
        _hasGitRepository = false;
        _primaryGitRepository = null;
        _readmeHtml = null;
        _readmeBranch = null;
        _changelog = [];
        _lastReleaseMetrics = null;

        await Task.WhenAll(
            LoadRecentPipelinesAsync(Project.Id),
            LoadReleaseDataAsync(Project.Id),
            LoadGitDataAsync(Project.Id));
    }

    private async Task LoadRecentPipelinesAsync(int projectId)
    {
        try
        {
            _recentPipelineRuns = (await Api.Pipelines.GetRecentPipelineRunsAsync(projectId))
                .OrderByDescending(run => run.StartedAt)
                .ToList();
        }
        catch
        {
            _recentPipelineRuns = [];
        }
        finally { _loadingRecentPipelines = false; }
    }

    private async Task StartPipelineHubAsync()
    {
        try
        {
            _pipelineHub = HubFactory.Create("pipelines");
            _pipelineHub.On<int, int>("PipelineRunStarted", OnPipelineRunStarted);
            _pipelineHub.On<int, PipelineStatus>("PipelineRunCompleted", (runId, _) => OnPipelineRunChangedAsync(runId));
            _pipelineHub.On<int>("PipelineRunCancelled", OnPipelineRunChangedAsync);
            _pipelineHub.On<int, string, string>("ApprovalRequired", (runId, _, _) => OnPipelineRunChangedAsync(runId));
            _pipelineHub.On<int, string, string>("ApprovalResolved", (runId, _, _) => OnPipelineRunChangedAsync(runId));
            _pipelineHub.RejoinOnReconnect(async () =>
            {
                await _pipelineHub.InvokeAsync("JoinPipelineUpdatesGroup");
                await ReloadRecentPipelinesAsync();
            });
            await _pipelineHub.StartAsync();
            await _pipelineHub.InvokeAsync("JoinPipelineUpdatesGroup");
        }
        catch { /* best-effort: the overview remains usable without live updates */ }
    }

    private Task OnPipelineRunStarted(int runId, int pipelineId) =>
        Project?.Pipelines.Any(pipeline => pipeline.Id == pipelineId) == true
            ? RequestRecentPipelineReloadAsync()
            : Task.CompletedTask;

    private async Task OnPipelineRunChangedAsync(int runId)
    {
        if (Project is null) return;
        try
        {
            var run = await Api.Pipelines.GetPipelineRunAsync(runId);
            if (run?.ProjectId == Project.Id) await RequestRecentPipelineReloadAsync();
        }
        catch (HttpRequestException) { }
    }

    private Task RequestRecentPipelineReloadAsync() =>
        InvokeAsync(() => _pipelineReload.RequestAsync(() => InvokeAsync(ReloadRecentPipelinesAsync)));

    private async Task ReloadRecentPipelinesAsync()
    {
        if (Project is null) return;
        await LoadRecentPipelinesAsync(Project.Id);
        StateHasChanged();
    }

    private async Task LoadGitDataAsync(int projectId)
    {
        List<GitLightRepoDto> repositories;
        try
        {
            repositories = await Api.Git.GetGitReposAsync(projectId);
        }
        catch
        {
            _recentCommits = [];
            _loadingRecentCommits = false;
            _gitStateLoaded = false;
            return;
        }

        _hasGitRepository = repositories.Count > 0 || !string.IsNullOrWhiteSpace(Project?.RepositoryUrl);
        _primaryGitRepository = repositories.FirstOrDefault(repository => !repository.IsEmpty)
            ?? repositories.FirstOrDefault();
        _gitStateLoaded = true;

        var commitTasks = repositories
            .Where(repository => !repository.IsEmpty)
            .Select(LoadRecentCommitsAsync);
        var readmeRepository = _primaryGitRepository;

        var commitsTask = Task.WhenAll(commitTasks);
        var readmeTask = readmeRepository is null ? Task.CompletedTask : LoadReadmeAsync(readmeRepository);
        var commitGroups = await commitsTask;
        _recentCommits = commitGroups
            .SelectMany(commits => commits)
            .OrderByDescending(commit => commit.AuthorDate)
            .Take(RecentItemLimit)
            .ToList();
        _loadingRecentCommits = false;
        await readmeTask;
    }

    private async Task<List<RecentCommitView>> LoadRecentCommitsAsync(GitLightRepoDto repository)
    {
        try
        {
            var result = await Api.Git.GetGitCommitsAsync(
                repository.Id,
                GitReference.AllBranches,
                pageSize: RecentItemLimit);
            return result.Items
                .Select(commit => RecentCommitView.FromCommit(repository, commit))
                .ToList();
        }
        catch { return []; }
    }

    /// <summary>Loads the project's releases once, then derives the changelog timeline and the
    /// "last release" metrics panel from the same list (one round-trip).</summary>
    private async Task LoadReleaseDataAsync(int projectId)
    {
        List<ReleaseDto> releases;
        try
        {
            var result = await Api.Projects.GetReleasesAsync(projectId: projectId);
            releases = result.Items;
        }
        // Best-effort enrichment (same isolation as GitRepositoryDetail's loaders): an expired
        // JWT (401), a transport error, or an unexpected payload must not collapse the overview.
        catch { _changelog = []; return; }

        _changelog = releases
            .Where(r => !string.IsNullOrWhiteSpace(r.Changelog))
            .OrderByDescending(r => r.PublishedAt ?? r.DetectedAt)
            .ToList();

        await LoadLastReleaseMetricsAsync(releases);
    }

    /// <summary>F-bis: fetches the run behind the most recent release and surfaces its coverage + LOC.
    /// Coverage comes from <c>CoverageSummary</c>; LOC from the <c>loc.total</c> run metric (published by a
    /// complexity step). The panel is shown only when at least one real metric exists.</summary>
    private async Task LoadLastReleaseMetricsAsync(List<ReleaseDto> releases)
    {
        var latest = releases
            .OrderByDescending(r => r.PublishedAt ?? r.DetectedAt)
            .FirstOrDefault(r => r.PipelineRunId.HasValue);
        if (latest?.PipelineRunId is not { } runId) return;

        PipelineRunDto? run;
        try { run = await Api.Pipelines.GetPipelineRunAsync(runId); }
        catch (HttpRequestException) { return; }
        if (run is null) return;

        var coverage = run.CoverageSummary?.LineRate;
        var locMetric = run.Metrics.FirstOrDefault(m => m.Key == "loc.total");
        int? loc = locMetric is null ? null : (int)Math.Round(locMetric.Value);

        if (coverage is null && loc is null) return; // nothing real to show
        _lastReleaseMetrics = new LastReleaseMetricsView(
            latest.Version, latest.PublishedAt ?? latest.DetectedAt, coverage, loc);
    }

    /// <summary>
    /// Renders the README of the project's first git repository (HEAD of its default branch),
    /// reusing the exact Markdig pipeline + sanitization from GitRepositoryDetail. No fabrication:
    /// when the project has no internal git repo, or the repo has no README.md, the README tab
    /// retains its explicit empty state.
    /// </summary>
    private async Task LoadReadmeAsync(GitLightRepoDto repo)
    {
        try
        {
            var branch = repo.DefaultBranch;
            var tree = await Api.Git.GetGitTreeAsync(repo.Id, branch);
            var readme = tree.FirstOrDefault(e =>
                e.Type == GitTreeEntryType.Blob &&
                e.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase));
            if (readme is null) return;

            var blob = await Api.Git.GetGitBlobAsync(repo.Id, branch, readme.Path);
            if (blob is null || blob.IsBinary || string.IsNullOrWhiteSpace(blob.Content)) return;

            _readmeHtml = Markdown.ToHtml(blob.Content, _markdownPipeline);
            _readmeBranch = branch;
        }
        // Best-effort: no internal git repo / no README / 401 / transport error → explicit empty state.
        catch { _readmeHtml = null; }
    }

    private static string CommitHref(RecentCommitView commit) =>
        $"/git-repositories/{commit.RepositoryId}/commits/{Uri.EscapeDataString(commit.Sha)}";

    private void OnRecentCommitClick(DataGridRowMouseEventArgs<RecentCommitView> args)
    {
        if (args.Data is not null) Nav.NavigateTo(CommitHref(args.Data));
    }

    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        if (_pipelineHub is not null)
        {
            try { await _pipelineHub.InvokeAsync("LeavePipelineUpdatesGroup"); } catch { /* best-effort */ }
            await _pipelineHub.DisposeAsync();
        }
    }
}
