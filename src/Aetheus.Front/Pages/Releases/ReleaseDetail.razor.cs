// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Git;
using Microsoft.Extensions.Logging;

namespace Aetheus.Front.Pages.Releases;

public partial class ReleaseDetail : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private Layout.ProjectNavContextService ProjectNav { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private ILogger<ReleaseDetail> Logger { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    [Parameter] public int ReleaseId { get; set; }

    private ReleaseDto? _release;
    private int? _loadedReleaseId;
    private int _loadGeneration;
    private bool _loading;
    private HubConnection? _hubConnection;
    private ReleaseRollbackPreviewDto? _rollbackPreview;
    private bool _canWrite;
    private List<string> _linkedCommitMessages = [];

    // The Deliverables panel groups what the release ships (artifacts) and where it targets
    // (environment). Hide it when the release has neither, so we never render an empty panel.
    private bool HasDeliverables =>
        _release is not null &&
        (_release.Artifacts.Count > 0 || !string.IsNullOrEmpty(_release.EnvironmentName));
    private bool CanRollback => _rollbackPreview?.CanRollback == true;
    private string RollbackDisabledReason => !_canWrite ? L["InsufficientPermissions"]
        : _rollbackPreview?.Reason ?? L["RollbackUnavailable"];

    protected override void OnInitialized()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        _canWrite = Permissions.CanWrite(ResourceType.Release);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedReleaseId == ReleaseId) return;
        _loadedReleaseId = ReleaseId;
        var releaseId = ReleaseId;
        var generation = ++_loadGeneration;
        bool IsCurrent() => generation == _loadGeneration && ReleaseId == releaseId;
        _loading = true;
        await StopHubAsync();
        if (!IsCurrent()) return;
        _release = null;
        _rollbackPreview = null;
        _linkedCommitMessages = [];
        try
        {
            ReleaseDto? release;
            try { release = await Api.Projects.GetReleaseAsync(releaseId); }
            catch (HttpRequestException) { release = null; }
            if (!IsCurrent()) return;

            _release = release;
            if (release is not null)
            {
                await LoadLinkedCommitMessagesAsync(release);
                if (!IsCurrent()) return;
                ProjectNav.Set(release.ProjectId);
                ReassertBreadcrumb(release);
                var rollbackPreview = await Api.Projects.GetRollbackPreviewAsync(releaseId);
                if (!IsCurrent()) return;
                _rollbackPreview = rollbackPreview;
                await StartHubAsync(release.ProjectId);
                if (!IsCurrent()) await StopHubAsync();
            }
        }
        finally
        {
            if (IsCurrent()) _loading = false;
        }
    }

    private void OnPermissionsChanged()
    {
        _canWrite = Permissions.CanWrite(ResourceType.Release);
        InvokeAsync(StateHasChanged);
    }

    private async Task RollbackAsync()
    {
        if (_release is null || _rollbackPreview is not { CanRollback: true, TargetVersion: { } targetVersion }) return;
        var pipelines = await Api.Pipelines.GetPipelinesAsync(pageSize: 100, projectId: _release.ProjectId);
        var request = await Dialog.OpenAsync<RollbackReleaseDialog>(L["Rollback"].Value,
            new Dictionary<string, object?>
            {
                ["SourceVersion"] = _release.Version,
                ["TargetVersion"] = targetVersion,
                ["DeploymentAge"] = RelativeTime.FormatAgo(L, _release.PublishedAt ?? _release.DetectedAt),
                ["ProjectId"] = _release.ProjectId,
                ["Pipelines"] = pipelines.Items
            }, new DialogOptions { Width = "520px", AutoFocusFirstElement = false });
        if (request is not RollbackReleaseRequest rollbackRequest) return;
        if (await Api.Projects.RollbackReleaseAsync(_release.Id, rollbackRequest) is not null)
            Toast.Success("RollbackQueued", _release.Version);
    }

    // The release status (Pending -> Deploying -> Deployed/Failed) flips while this page is open.
    // Subscribe to the project's release group so the badge updates live instead of needing a refresh.
    private async Task StartHubAsync(int projectId)
    {
        try
        {
            _hubConnection = HubFactory.Create("releases");
            _hubConnection.On<ReleaseDto>("ReleaseStatusChanged", OnReleaseChanged);
            _hubConnection.RejoinOnReconnect(() => InvokeAsync(async () =>
            {
                await _hubConnection.InvokeAsync("JoinProjectGroup", projectId);
                await ReloadAsync();
            }));
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinProjectGroup", projectId);
        }
        catch
        {
            // SignalR is best-effort: fall back to the static load.
        }
    }

    internal Task OnReleaseChanged(ReleaseDto release)
    {
        if (release.Id != ReleaseId) return Task.CompletedTask;
        return InvokeAsync(async () =>
        {
            _release = release;
            ReassertBreadcrumb(release);
            await LoadLinkedCommitMessagesAsync(release);
            StateHasChanged();
        });
    }

    private async Task ReloadAsync()
    {
        try
        {
            _release = await Api.Projects.GetReleaseAsync(ReleaseId);
            if (_release is not null)
            {
                ReassertBreadcrumb(_release);
                await LoadLinkedCommitMessagesAsync(_release);
            }
        }
        catch (HttpRequestException ex)
        {
            // Transient: keep the prior release, but leave a debug signal so a stuck status
            // (e.g. Deploying that never refreshes) is diagnosable instead of silently swallowed.
            Logger.LogWarning(ex, "Release {ReleaseId} reload failed; keeping the previously loaded snapshot.", ReleaseId);
        }
        StateHasChanged();
    }

    private void ReassertBreadcrumb(ReleaseDto release) => Breadcrumb.Set(
        new BreadcrumbItem(L["Projects"], "/projects"),
        new BreadcrumbItem(release.ProjectName, $"/projects/{release.ProjectId}/overview"),
        new BreadcrumbItem(L["Releases"], $"/projects/{release.ProjectId}/releases"),
        new BreadcrumbItem(release.Version));

    public async ValueTask DisposeAsync()
    {
        _loadGeneration++;
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        await StopHubAsync();
    }

    private async Task StopHubAsync()
    {
        if (_hubConnection is not null)
        {
            if (_release is not null)
            {
                try { await _hubConnection.InvokeAsync("LeaveProjectGroup", _release.ProjectId); }
                catch { /* best-effort */ }
            }
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    private sealed record ChangelogEntry(string? Type, string Text);

    private IReadOnlyList<ChangelogEntry> VisibleChangelogEntries
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_release?.Changelog))
                return ParseChangelogEntries(_release.Changelog);

            return _release?.Commits
                .Where(commit => !string.IsNullOrWhiteSpace(commit.Message))
                .Select(commit => commit.Message!)
                .Concat(_linkedCommitMessages)
                .Distinct(StringComparer.Ordinal)
                .SelectMany(ParseChangelogEntries)
                .ToList() ?? [];
        }
    }

    private async Task LoadLinkedCommitMessagesAsync(ReleaseDto release)
    {
        _linkedCommitMessages = [];
        if (!string.IsNullOrWhiteSpace(release.Changelog)) return;
        _linkedCommitMessages.AddRange(release.Commits
            .Select(commit => commit.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message!));

        var missingShas = release.Commits
            .Where(commit => string.IsNullOrWhiteSpace(commit.Message))
            .Select(commit => commit.Sha)
            .Where(sha => !string.IsNullOrWhiteSpace(sha))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();
        if (missingShas.Count == 0) return;

        try
        {
            var repositories = await Api.Git.GetGitReposAsync(release.ProjectId);
            var repositoryId = GitRepositorySelection.Resolve(repositories, release.RepositoryUrl);
            if (repositoryId is null) return;

            var messages = await Api.Git.GetGitCommitMessagesAsync(repositoryId.Value, missingShas);
            foreach (var sha in missingShas)
                if (messages.TryGetValue(sha, out var message) && !string.IsNullOrWhiteSpace(message))
                    _linkedCommitMessages.Add(message);
        }
        catch (HttpRequestException)
        {
            // The release remains usable when historical Git metadata is unavailable.
        }
    }

    private static List<ChangelogEntry> ParseChangelogEntries(string changelog)
    {
        var entries = new List<ChangelogEntry>();
        foreach (var line in changelog.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimStart('-', '*', ' ');
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            string? type = null;
            var text = trimmed;
            if (trimmed.StartsWith("feat", StringComparison.OrdinalIgnoreCase))
                type = "feat";
            else if (trimmed.StartsWith("fix", StringComparison.OrdinalIgnoreCase))
                type = "fix";
            else if (trimmed.StartsWith("chore", StringComparison.OrdinalIgnoreCase))
                type = "chore";
            else if (trimmed.StartsWith("docs", StringComparison.OrdinalIgnoreCase))
                type = "docs";
            else if (trimmed.StartsWith("refactor", StringComparison.OrdinalIgnoreCase))
                type = "refactor";
            else if (trimmed.StartsWith("perf", StringComparison.OrdinalIgnoreCase))
                type = "perf";
            else if (trimmed.StartsWith("test", StringComparison.OrdinalIgnoreCase))
                type = "test";

            if (type is not null)
            {
                var colonIdx = trimmed.IndexOf(':', StringComparison.Ordinal);
                if (colonIdx > 0 && colonIdx < 20)
                    text = trimmed[(colonIdx + 1)..].TrimStart();
            }

            entries.Add(new ChangelogEntry(type, text));
        }

        if (entries.Count == 0)
            entries.Add(new ChangelogEntry(null, changelog.Trim()));

        return entries;
    }

    // Maps a conventional-commit type to a RadzenTimeline point colour (the timeline replaces the old
    // hand-built rail/dot/line markup, per the project's RadzenTimeline-for-changelogs rule).
    private static PointStyle GetChangelogPointStyle(string? type) => type switch
    {
        "feat" => PointStyle.Success,
        "fix" => PointStyle.Danger,
        "perf" => PointStyle.Warning,
        "chore" or "refactor" => PointStyle.Secondary,
        "docs" => PointStyle.Info,
        _ => PointStyle.Light
    };

    private static BadgeStyle GetChangelogBadge(string? type) => type switch
    {
        "feat" => BadgeStyle.Success,
        "fix" => BadgeStyle.Danger,
        "perf" => BadgeStyle.Warning,
        "docs" => BadgeStyle.Info,
        _ => BadgeStyle.Light
    };
}
