// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Analysis;

public partial class AnalysisPortfolio : ComponentBase, IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;

    private Radzen.Blazor.RadzenDataGrid<AnalysisPortfolioRowDto>? _grid;
    private List<AnalysisPortfolioRowDto> _rows = [];
    private List<AnalysisPortfolioProjectDto> _projectSummaries = [];
    private List<ProjectDto> _projects = [];
    private readonly AnalysisCategory[] _categories = Enum.GetValues<AnalysisCategory>();
    private int _count;
    private string? _search;
    private int? _organizationId;
    private int? _projectId;
    private int? _pipelineId;
    private AnalysisCategory? _category;
    private string? _branch;
    private string? _commit;
    private DateTime? _from;
    private DateTime? _to;
    private HubConnection? _hubConnection;
    private int PortfolioReportCount => _projectSummaries.Sum(project => project.ReportCount);

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var projectsTask = Api.Projects.GetProjectsAsync(1, 200);
            var summariesTask = Api.Analysis.GetAnalysisPortfolioProjectsAsync();
            await Task.WhenAll(projectsTask, summariesTask);
            _projects = (await projectsTask).Items;
            _projectSummaries = await summariesTask;
        }
        catch (HttpRequestException)
        {
            _projects = [];
            _projectSummaries = [];
        }

        try
        {
            // Radzen does not invoke LoadData on every first render (notably when the
            // surrounding async content changes). Prime the first page explicitly so
            // the portfolio never shows populated project summaries above an empty grid.
            await LoadDataAsync(new LoadDataArgs { Skip = 0, Top = 25 });
        }
        catch (HttpRequestException)
        {
            _rows = [];
            _count = 0;
        }

        await StartRealtimeAsync();
    }

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        var size = Math.Clamp(args.Top ?? 25, 1, 200);
        var page = ((args.Skip ?? 0) / size) + 1;
        var sort = args.Sorts?.FirstOrDefault();
        var result = await Api.Analysis.GetAnalysisPortfolioAsync(page, size, _search, _organizationId, _projectId,
            _pipelineId, _category, _branch, _commit, _from, _to, sort?.Property,
            sort?.SortOrder == SortOrder.Descending);
        _rows = result.Items;
        _count = result.TotalCount;
    }

    private Task ReloadAsync() => _grid?.FirstPage(true) ?? Task.CompletedTask;

    private async Task StartRealtimeAsync()
    {
        try
        {
            _hubConnection = HubFactory.Create("entities");
            _hubConnection.On<ResourceType, int, string>("EntityChanged", (type, _, _) =>
                type == ResourceType.Project
                    ? InvokeAsync(ReloadAsync)
                    : Task.CompletedTask);
            _hubConnection.RejoinOnReconnect(() => InvokeAsync(async () =>
            {
                await _hubConnection.InvokeAsync("JoinEntityUpdates", ResourceType.Project);
                await ReloadAsync();
            }));
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinEntityUpdates", ResourceType.Project);
        }
        catch { /* SignalR is best-effort; filters remain manually reloadable. */ }
    }

    private async Task ClearFiltersAsync()
    {
        _search = null;
        _organizationId = null;
        _projectId = null;
        _pipelineId = null;
        _category = null;
        _branch = null;
        _commit = null;
        _from = null;
        _to = null;
        if (_grid is not null) await _grid.FirstPage(true);
    }

    private void OpenRun(DataGridRowMouseEventArgs<AnalysisPortfolioRowDto> args)
    {
        var row = args.Data;
        if (row?.PipelineRunId.HasValue == true) Navigation.NavigateTo($"/pipelines/runs/{row.PipelineRunId}");
    }

    private string Posture(AnalysisCategory category) => category is AnalysisCategory.Sast
        or AnalysisCategory.Secrets or AnalysisCategory.Dependencies or AnalysisCategory.Container
        or AnalysisCategory.InfrastructureAsCode or AnalysisCategory.Sbom or AnalysisCategory.Dast
        ? L["Security"] : L["Quality"];

    private static BadgeStyle PostureStyle(AnalysisCategory category) => category is AnalysisCategory.Sast
        or AnalysisCategory.Secrets or AnalysisCategory.Dependencies or AnalysisCategory.Container
        or AnalysisCategory.InfrastructureAsCode or AnalysisCategory.Sbom or AnalysisCategory.Dast
        ? BadgeStyle.Danger : BadgeStyle.Info;

    private static BadgeStyle GateStyle(AnalysisGateStatus? status) => status switch
    {
        AnalysisGateStatus.Passed => BadgeStyle.Success,
        AnalysisGateStatus.Warning => BadgeStyle.Warning,
        AnalysisGateStatus.Blocked or AnalysisGateStatus.Error => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };

    private static BadgeStyle GradeStyle(AnalysisGrade? grade) => grade switch
    {
        AnalysisGrade.A or AnalysisGrade.B => BadgeStyle.Success,
        AnalysisGrade.C => BadgeStyle.Info,
        AnalysisGrade.D or AnalysisGrade.E => BadgeStyle.Warning,
        AnalysisGrade.F => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };

    private static string GradeText(AnalysisPortfolioProjectDto project) =>
        project.Grade?.OverallGrade?.ToString() ?? "-";

    private static string ShortCommit(string? commit) => string.IsNullOrWhiteSpace(commit)
        ? "-" : commit[..Math.Min(12, commit.Length)];

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is null) return;
        await _hubConnection.LeaveEntityUpdatesAndDisposeAsync(ResourceType.Project);
        _hubConnection = null;
    }
}
