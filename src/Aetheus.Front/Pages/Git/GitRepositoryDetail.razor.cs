// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Markdig;
namespace Aetheus.Front.Pages.Git;

public partial class GitRepositoryDetail : IAsyncDisposable
{
    [Parameter] public int Id { get; set; }
    [SupplyParameterFromQuery(Name = "search")] public string? CommitSearch { get; set; }
    [SupplyParameterFromQuery(Name = "branch")] public string? BranchSearch { get; set; }
    [SupplyParameterFromQuery(Name = "ref")] public string? FileRef { get; set; }
    [SupplyParameterFromQuery(Name = "path")] public string? FilePath { get; set; }
    [SupplyParameterFromQuery(Name = "line")] public int? FileLine { get; set; }
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;
    [Inject] private ILogger<GitRepositoryDetail> Logger { get; set; } = default!;
    private string CommitsCacheKey(int page, int pageSize) =>
        GitRepositoryCacheKeys.Commits(Id, _selectedRef, page, pageSize, _commitSearch);
    private string PrsCacheKey(int page, int pageSize, string? sortBy = null, bool sortDescending = true) =>
        GitRepositoryCacheKeys.PullRequests(Id, page, pageSize, sortBy, sortDescending);
    private GitRepositoryRealtimeSubscription? _realtime;
    private bool _canWrite;
    private int? _loadedId;
    private string? _loadedCommitSearch, _loadedBranchSearch, _loadedFileRef, _loadedFilePath;
    private int? _loadedFileLine;
    private int _dataGeneration;
    private GitRepositoryDetailActions Actions => new(Api, Dialog, Toast, L);
    private GitLightRepoDto? _repo;
    private bool _loading = true;
    private int _selectedTab;
    // Ordered tab slugs for UrlSyncedTabs (?tab=). Mirrors the markup; Readme is conditional.
    private string[] TabSlugs => string.IsNullOrEmpty(_readmeHtml)
        ? ["commits", "branches", "tags", "files", "pulls", "graph", "protection"]
        : ["commits", "branches", "tags", "files", "pulls", "graph", "readme", "protection"];
    // Commits
    private List<GitLightCommitDto> _commits = [];
    private int _commitsTotalCount;
    private bool _commitsLoading;
    private bool _commitsError;
    private string? _selectedRef;
    private string? _commitSearch;
    private sealed record CommitRefOption(string Text, string Value);
    private IEnumerable<CommitRefOption> CommitRefOptions =>
        [new(L["AllBranches"], GitReference.AllBranches), .. _branches.Select(branch => new CommitRefOption(branch.Name, branch.Name))];
    // Branches
    private List<GitLightBranchDto> _branches = [];
    private readonly GitRepositoryPageState _branchPage = new();
    // Tags
    private List<GitLightTagDto> _tags = [];
    private readonly GitRepositoryPageState _tagPage = new();
    // Files
    private List<GitLightTreeEntryDto> _treeEntries = [];
    private bool _treeLoading;
    private bool _treeError;
    private int _treeTotalCount;
    private string? _treeRef;
    private string? _treePath;
    private GitLightBlobDto? _blobContent;
    private readonly string _monacoElementId = $"monaco-viewer-{Guid.NewGuid():N}";
    private bool _pendingMonacoInit;
    private bool _monacoInitialized;
    // Pull Requests
    private List<InternalPullRequestDto> _pullRequests = [];
    private int _prTotalCount;
    private bool _prsLoading;
    private bool _prsError;
    // README
    private string? _readmeContent;
    private string? _readmeHtml;
    // Branch Protection
    private List<BranchProtectionRuleDto> _protectionRules = [];
    private readonly GitRepositoryPageState _protectionPage = new();
    // Commit Graph
    private List<GitLightCommitDto> _graphCommits = [];
    private bool _graphLoading;
    private bool _graphError;
    // Blame
    private List<GitLightBlameLine>? _blameLines;
    private string? _blameFormatted;
    protected override Task OnInitializedAsync()
    {
        // Subscribe BEFORE loading: MainLayout resolves permissions in parallel with route
        // activation, so this page can mount before they're ready. Without the subscription,
        // _canWrite would stay false until the next navigation, stranding the write actions as
        // permanently disabled. Mirrors the GitRepositories list pattern.
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();
        return Task.CompletedTask;
    }
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedId == Id && string.Equals(_loadedCommitSearch, CommitSearch, StringComparison.Ordinal)
            && string.Equals(_loadedBranchSearch, BranchSearch, StringComparison.Ordinal)
            && string.Equals(_loadedFileRef, FileRef, StringComparison.Ordinal)
            && string.Equals(_loadedFilePath, FilePath, StringComparison.Ordinal)
            && _loadedFileLine == FileLine) return;
        var previousId = _loadedId;
        _loadedId = Id;
        _loadedCommitSearch = CommitSearch;
        _loadedBranchSearch = BranchSearch;
        _loadedFileRef = FileRef;
        _loadedFilePath = FilePath;
        _loadedFileLine = FileLine;
        _commitSearch = CommitSearch;
        _dataGeneration++;
        await ResetRepositoryStateAsync(previousId);
        await LoadData();
        if (_repo is not null) await ConnectHub();
    }
    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }
    private void RefreshCanWrite() => _canWrite = Permissions.CanWrite(ResourceType.Project);
    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        _dataGeneration++;
        await DisposeHubAsync(_loadedId);
        await DisposeMonacoAsync();
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingMonacoInit && _blobContent is not null)
        {
            _pendingMonacoInit = false;
            var lang = GitRepositoryViewHelpers.InferLanguage(_blobContent.Path);
            await JS.InvokeVoidAsync("monacoInterop.initReadOnly", _monacoElementId, _blobContent.Content, lang, true, FileLine);
            _monacoInitialized = true;
        }
    }
    private async Task ResetRepositoryStateAsync(int? previousId)
    {
        await DisposeHubAsync(previousId);
        await DisposeMonacoAsync();
        _repo = null;
        _selectedRef = null;
        _treeRef = null;
        _treePath = null;
        _blobContent = null;
        _blameLines = null;
        _readmeContent = null;
        _readmeHtml = null;
        _commits = [];
        _branches = [];
        _tags = [];
        _treeEntries = [];
        _pullRequests = [];
        _protectionRules = [];
        _graphCommits = [];
    }
    private async Task DisposeHubAsync(int? repositoryId)
    {
        _ = repositoryId;
        var realtime = _realtime;
        _realtime = null;
        if (realtime is not null) await realtime.DisposeAsync();
    }
    private async Task DisposeMonacoAsync()
    {
        if (!_monacoInitialized) return;
        try { await JS.InvokeVoidAsync("monacoInterop.disposeEditor", _monacoElementId); }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException) { }
        _monacoInitialized = false;
    }
    private async Task LoadData()
    {
        var generation = _dataGeneration;
        var repositoryId = Id;
        _loading = true;
        // The initial repo fetch was previously unguarded - when the user lands here with an
        // expired token, GetFromJsonAsync throws HttpRequestException (401), the exception
        // bubbles through OnInitializedAsync, and Blazor's ErrorBoundary takes over with a
        // generic "Une erreur est survenue" screen even though the auth handler is already
        // navigating to /login. Same behaviour for any non-success status the API returns
        // (403, 5xx). Containing the exception here lets the page fall through to its
        // existing `_repo is null → NotFound` branch and lets the auth redirect complete
        // cleanly. The sub-resource loaders below already use SafeLoad for the same reason.
        GitLightRepoDto? repository;
        try { repository = await Api.Git.GetGitRepoAsync(repositoryId); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            GitLoadFailureLogger.Log(Logger, ex, "repository", detailPage: true);
            repository = null;
        }
        if (generation != _dataGeneration || repositoryId != Id) return;
        _repo = repository;
        _loading = false;
        if (_repo is null) return;
        Breadcrumb.Set(
            new BreadcrumbItem(L["Projects"], "/projects"),
            new BreadcrumbItem(_repo.ProjectName ?? $"{L["Project"]} #{_repo.ProjectId}", $"/projects/{_repo.ProjectId}/overview"),
            new BreadcrumbItem(L["GitRepositories"], $"/git-repositories?projectId={_repo.ProjectId}"),
            new BreadcrumbItem(_repo.Name));
        _selectedRef ??= GitReference.AllBranches;
        _treeRef ??= string.IsNullOrWhiteSpace(FileRef) ? _repo.DefaultBranch : FileRef;
        // Isolate each sub-resource: one slow/failed loader (e.g. a git timeout on
        // commits) must not collapse the whole page to the ErrorBoundary.
        await Task.WhenAll(
            SafeLoad(LoadBranches), SafeLoad(LoadTags), SafeLoad(LoadCommits), SafeLoad(LoadTree),
            SafeLoad(LoadPrs), SafeLoad(LoadProtectionRules), SafeLoad(LoadGraph));
        if (!string.IsNullOrWhiteSpace(FilePath)) await SafeLoad(OpenDeepLinkedFileAsync);
        await SafeLoad(LoadReadme);
    }
    private async Task SafeLoad(Func<Task> load)
    {
        // Loaders surface their own inline error state; this just contains the
        // exception so a single failure doesn't crash the page.
        try { await load().ConfigureAwait(false); }
        catch (HttpRequestException ex) { GitLoadFailureLogger.Log(Logger, ex, "sub-resource", detailPage: true); }
        catch (JsonException ex) { GitLoadFailureLogger.Log(Logger, ex, "sub-resource", detailPage: true); }
        catch (TaskCanceledException) { }
    }
    private async Task LoadPageAsync<T>(
        GitRepositoryPageState state, Func<Task<PaginatedResult<T>>> fetch,
        Action<List<T>> apply)
    {
        var generation = _dataGeneration;
        state.Loading = true;
        state.Error = false;
        try
        {
            var result = await fetch();
            if (generation != _dataGeneration) return;
            apply(result.Items);
            state.TotalCount = result.TotalCount;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            if (generation != _dataGeneration) return;
            GitLoadFailureLogger.Log(Logger, ex, "paged sub-resource", detailPage: true);
            apply([]);
            state.TotalCount = 0;
            state.Error = true;
        }
        finally
        {
            if (generation == _dataGeneration) state.Loading = false;
        }
    }

    // ── Commits ──────────────────────────────────────────────────────
    private async Task LoadCommits()
    {
        _commitsLoading = true;
        _commitsError = false;
        try
        {
            var result = await Api.Git.GetGitCommitsAsync(Id, _selectedRef, search: _commitSearch);
            _commits = result.Items;
            _commitsTotalCount = result.TotalCount;
        }
        catch
        {
            _commits = [];
            _commitsTotalCount = 0;
            _commitsError = true;
        }
        finally
        {
            _commitsLoading = false;
        }
    }
    private async Task OnLoadCommits(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest(30);
        // S-TECH-SWGT: render the last cached page instantly, then revalidate; the broad catch keeps the
        // bespoke _commitsError state intact (and, on a cache hit, leaves the cached rows on screen).
        var key = CommitsCacheKey(page, pageSize);
        var hit = Cache.TryGet<PaginatedResult<GitLightCommitDto>>(key, out var cached) && cached is not null;
        if (hit)
        {
            _commits = cached!.Items;
            _commitsTotalCount = cached.TotalCount;
            _commitsError = false;
        }
        _commitsLoading = !hit;
        try
        {
            var result = await Api.Git.GetGitCommitsAsync(Id, _selectedRef, page, pageSize, _commitSearch);
            _commits = result.Items;
            _commitsTotalCount = result.TotalCount;
            _commitsError = false;
            Cache.Set(key, result);
        }
        catch
        {
            if (!hit)
            {
                _commits = [];
                _commitsTotalCount = 0;
            }
            _commitsError = true;
        }
        finally
        {
            _commitsLoading = false;
        }
    }
    // ── Branches ─────────────────────────────────────────────────────
    private Task LoadBranches() => LoadBranchesPageAsync(1, 25);
    private Task LoadBranchesPageAsync(int page, int pageSize, string? sortBy = null, bool sortDescending = false) => LoadPageAsync(
        _branchPage,
        () => Api.Git.GetGitBranchesPageAsync(Id, page, pageSize, BranchSearch, sortBy, sortDescending),
        items => _branches = items);
    private async Task OnLoadBranches(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = args.ToSortRequest(nameof(GitLightBranchDto.Name));
        await LoadBranchesPageAsync(page, pageSize, sortBy, sortDescending);
    }
    private async Task ShowCreateBranchDialog()
    {
        if (await Actions.CreateBranchAsync(Id)) await LoadBranches();
    }
    private async Task DeleteBranch(GitLightBranchDto branch)
    {
        if (await Actions.DeleteBranchAsync(Id, branch)) await LoadBranches();
    }
    // ── Tags ─────────────────────────────────────────────────────────
    private Task LoadTags() => LoadTagsPageAsync(1, 25);
    private Task LoadTagsPageAsync(int page, int pageSize, string? sortBy = null, bool sortDescending = false) => LoadPageAsync(
        _tagPage,
        () => Api.Git.GetGitTagsPageAsync(Id, page, pageSize, search: null, sortBy, sortDescending),
        items => _tags = items);
    private async Task OnLoadTags(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = args.ToSortRequest(nameof(GitLightTagDto.Name));
        await LoadTagsPageAsync(page, pageSize, sortBy, sortDescending);
    }
    private async Task ShowCreateTagDialog()
    {
        if (await Actions.CreateTagAsync(Id)) await LoadTags();
    }
    private async Task DeleteTag(GitLightTagDto tag)
    {
        if (await Actions.DeleteTagAsync(Id, tag)) await LoadTags();
    }
    // ── File browser ─────────────────────────────────────────────────
    private static readonly Markdig.MarkdownPipeline _markdownPipeline =
        new Markdig.MarkdownPipelineBuilder().DisableHtml().Build();
    private async Task LoadReadme()
    {
        _readmeContent = null;
        _readmeHtml = null;
        var readme = _treeEntries.FirstOrDefault(e =>
            e.Type == GitTreeEntryType.Blob &&
            e.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase));
        if (readme is null) return;
        var blob = await Api.Git.GetGitBlobAsync(Id, _treeRef ?? _repo!.DefaultBranch, readme.Path);
        if (blob is not null && !blob.IsBinary)
        {
            _readmeContent = blob.Content;
            _readmeHtml = Markdig.Markdown.ToHtml(_readmeContent ?? string.Empty, _markdownPipeline);
        }
    }
    private async Task LoadTree()
    {
        await DisposeMonacoAsync();
        _blobContent = null;
        await LoadTreePageAsync(() => Api.Git.GetGitTreePageAsync(Id, _treeRef, _treePath));
    }
    private async Task OnLoadTree(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = args.ToSortRequest(nameof(GitLightTreeEntryDto.Name));
        await LoadTreePageAsync(
            () => Api.Git.GetGitTreePageAsync(Id, _treeRef, _treePath, page, pageSize, search: null, sortBy, sortDescending));
    }
    private async Task LoadTreePageAsync(
        Func<Task<PaginatedResult<GitLightTreeEntryDto>>> loadAsync)
    {
        _treeLoading = true;
        _treeError = false;
        try
        {
            var result = await loadAsync();
            _treeEntries = result.Items;
            _treeTotalCount = result.TotalCount;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _treeEntries = [];
            _treeTotalCount = 0;
            _treeError = true;
        }
        finally { _treeLoading = false; }
    }
    private async Task NavigateToEntry(GitLightTreeEntryDto entry)
    {
        if (entry.Type == GitTreeEntryType.Tree)
        {
            _treePath = entry.Path;
            await LoadTree();
        }
        else
        {
            await DisposeMonacoAsync();
            _treeLoading = true;
            _treeError = false;
            try
            {
                _blobContent = await Api.Git.GetGitBlobAsync(
                    Id, _treeRef ?? _repo!.DefaultBranch, entry.Path);
                _pendingMonacoInit = _blobContent is not null && !_blobContent.IsBinary;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                _blobContent = null;
                _treeError = true;
            }
            finally { _treeLoading = false; }
        }
    }

    private async Task OpenDeepLinkedFileAsync()
    {
        var path = FilePath?.Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(path) || path.EndsWith('/')) return;
        await DisposeMonacoAsync();
        _treePath = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(_treePath)) _treePath = null;
        _blobContent = await Api.Git.GetGitBlobAsync(Id, _treeRef ?? _repo!.DefaultBranch, path);
        _pendingMonacoInit = _blobContent is not null && !_blobContent.IsBinary;
    }
    private async Task CloseBlobAsync()
    {
        await DisposeMonacoAsync();
        _blobContent = null;
        _blameLines = null;
        _blameFormatted = null;
    }
    private async Task NavigateUp()
    {
        if (string.IsNullOrEmpty(_treePath)) return;
        var lastSlash = _treePath.LastIndexOf('/');
        _treePath = lastSlash > 0 ? _treePath[..lastSlash] : null;
        await LoadTree();
    }
    // ── Pull Requests ────────────────────────────────────────────────
    private async Task LoadPrs()
    {
        _prsLoading = true;
        _prsError = false;
        try
        {
            var result = await Api.Git.GetGitPullRequestsAsync(Id);
            _pullRequests = result.Items;
            _prTotalCount = result.TotalCount;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _pullRequests = [];
            _prTotalCount = 0;
            _prsError = true;
        }
        finally { _prsLoading = false; }
    }
    private async Task OnLoadPrs(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = args.ToSortRequest(nameof(InternalPullRequestDto.CreatedAt), fallbackDescending: true);
        // S-TECH-SWGT: SWR for the pull-requests grid - instant paint from cache, then refresh.
        var key = PrsCacheKey(page, pageSize, sortBy, sortDescending);
        var hit = Cache.TryGet<PaginatedResult<InternalPullRequestDto>>(key, out var cached) && cached is not null;
        if (hit)
        {
            _pullRequests = cached!.Items;
            _prTotalCount = cached.TotalCount;
        }
        _prsLoading = !hit;
        _prsError = false;
        try
        {
            var result = await Api.Git.GetGitPullRequestsAsync(Id, page, pageSize, search: null, status: null, sortBy, sortDescending);
            _pullRequests = result.Items;
            _prTotalCount = result.TotalCount;
            Cache.Set(key, result);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            if (!hit)
            {
                _pullRequests = [];
                _prTotalCount = 0;
            }
            _prsError = true;
        }
        finally { _prsLoading = false; }
    }
    private async Task ShowCreatePrDialog()
    {
        if (await Actions.CreatePullRequestAsync(Id)) await LoadPrs();
    }
    private async Task MergePr(InternalPullRequestDto pr)
    {
        if (await Actions.MergePullRequestAsync(Id, pr)) await LoadPrs();
    }
    private async Task ClosePr(InternalPullRequestDto pr)
    {
        if (await Actions.ClosePullRequestAsync(Id, pr)) await LoadPrs();
    }
    // ── Commit Graph ───────────────────────────────────────────────
    private async Task LoadGraph()
    {
        _graphLoading = true;
        _graphError = false;
        try { _graphCommits = await Api.Git.GetGitGraphDataAsync(Id); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _graphCommits = [];
            _graphError = true;
        }
        finally { _graphLoading = false; }
    }
    // ── Branch Protection ───────────────────────────────────────────
    private Task LoadProtectionRules() => LoadProtectionRulesPageAsync(1, 25);
    private Task LoadProtectionRulesPageAsync(int page, int pageSize, string? sortBy = null, bool sortDescending = false) => LoadPageAsync(
        _protectionPage,
        () => Api.Git.GetGitBranchProtectionRulesPageAsync(Id, page, pageSize, search: null, sortBy, sortDescending),
        items => _protectionRules = items);
    private async Task OnLoadProtectionRules(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = args.ToSortRequest(nameof(BranchProtectionRuleDto.Pattern));
        await LoadProtectionRulesPageAsync(page, pageSize, sortBy, sortDescending);
    }
    private async Task ShowAddProtectionRuleDialog()
    {
        if (await Actions.AddProtectionRuleAsync(Id)) await LoadProtectionRules();
    }
    private async Task DeleteProtectionRule(BranchProtectionRuleDto rule)
    {
        if (await Actions.DeleteProtectionRuleAsync(Id, rule)) await LoadProtectionRules();
    }
    private async Task UpdateProtectionRule(BranchProtectionRuleDto rule, bool? preventDeletion = null, bool? preventForcePush = null, bool? requirePr = null)
    {
        if (await Actions.UpdateProtectionRuleAsync(
            Id, rule, preventDeletion, preventForcePush, requirePr))
            await LoadProtectionRules();
    }
    private bool IsBranchProtected(string branchName)
    {
        return _protectionRules.Any(r => GitRepositoryViewHelpers.BranchMatchesPattern(branchName, r.Pattern));
    }
    // ── Blame ───────────────────────────────────────────────────────
    private async Task ToggleBlame()
    {
        if (_blameLines is not null)
        {
            _blameLines = null;
            _blameFormatted = null;
            _pendingMonacoInit = true;
            return;
        }
        if (_blobContent is null) return;
        _blameLines = await Api.Git.GetGitBlameAsync(Id, _treeRef ?? _repo!.DefaultBranch, _blobContent.Path);
        if (_blameLines.Count > 0)
        {
            _blameFormatted = GitRepositoryViewHelpers.FormatBlame(_blameLines);
            await JS.InvokeVoidAsync("monacoInterop.disposeEditor", _monacoElementId);
            _monacoInitialized = false;
        }
        else
        {
            _blameLines = null;
        }
    }
    private static BadgeStyle GetPrStatusBadge(PullRequestStatus status) => GitRepositoryViewHelpers.GetPullRequestStatusBadge(status);
    private async Task CopyCloneUrl()
    {
        if (_repo?.CloneUrl is null) return;
        var copied = await JS.InvokeAsync<bool>("Aetheus.copyToClipboard", _repo.CloneUrl);
        if (copied)
            Toast.Success(L["Copied"].Value, _repo.CloneUrl);
    }
    private async Task ConnectHub()
    {
        _realtime = new GitRepositoryRealtimeSubscription(HubFactory);
        await _realtime.StartAsync(
            Id,
            () => InvokeAsync(async () => { await LoadBranches(); StateHasChanged(); }),
            () => InvokeAsync(async () => { await LoadTags(); StateHasChanged(); }),
            () => InvokeAsync(async () => { Cache.InvalidatePrefix($"git-commits:{Id}:"); await LoadCommits(); StateHasChanged(); }),
            () => InvokeAsync(async () => { Cache.InvalidatePrefix($"git-prs:{Id}:"); await LoadPrs(); StateHasChanged(); }),
            () => InvokeAsync(async () => { _repo = await Api.Git.GetGitRepoAsync(Id); StateHasChanged(); }));
    }
}
