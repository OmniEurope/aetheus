// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectArtifactsSection
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;

    [Parameter, EditorRequired] public int ProjectId { get; set; }

    private List<PipelineArtifactDto> _artifacts = [];
    private int? _loadedProjectId;
    private int _totalCount;
    private int _pageSize = 25;
    private bool _loading = true;
    private ArtifactRetentionPolicy? _selectedPolicy;

    private readonly List<PolicyOption> _policyOptions =
    [
        new("Build", ArtifactRetentionPolicy.Build),
        new("Deployed", ArtifactRetentionPolicy.Deployed),
        new("Released", ArtifactRetentionPolicy.Released)
    ];

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedProjectId == ProjectId) return;
        _loadedProjectId = ProjectId;
        // Stale-while-revalidate: seed from cache (and drop the section spinner) so a revisit paints
        // the previous rows instantly; LoadDataAsync then revalidates in the background.
        Cache.Seed<PaginatedResult<PipelineArtifactDto>>(
            CacheKey(1, _pageSize, _selectedPolicy),
            result => { ApplyArtifacts(result); _loading = false; });
        try
        {
            await LoadDataAsync();
        }
        catch (HttpRequestException) { } // 401 on expired JWT - redirect handled by AuthProvider
    }

    private string CacheKey(int page, int pageSize, ArtifactRetentionPolicy? policy) =>
        $"artifacts:{ProjectId}:{page}:{pageSize}:{policy}";

    private void ApplyArtifacts(PaginatedResult<PipelineArtifactDto> result)
    {
        _artifacts = result.Items;
        _totalCount = result.TotalCount;
    }

    // Section-level (re)load: drives the full-section spinner via _loading. Reused by the Refresh
    // button and the policy filter; a cache hit skips the spinner, a cold/filtered view shows it.
    private Task LoadDataAsync()
    {
        var projectId = ProjectId;
        return Cache.RevalidateAsync(
            CacheKey(1, _pageSize, _selectedPolicy),
            () => Api.ServerTools.GetProjectArtifactsAsync(projectId, 1, _pageSize, _selectedPolicy),
            result => { if (ProjectId == projectId) ApplyArtifacts(result); },
            loading => { if (ProjectId == projectId) _loading = loading; },
            () => InvokeAsync(StateHasChanged));
    }

    // Grid paging: must NOT toggle the section spinner (_loading hides the whole grid), so the
    // loading setter is a no-op here; the cache still smooths the paint and revalidates.
    private Task OnLoadData(LoadDataArgs args)
    {
        var page = (args.Skip ?? 0) / _pageSize + 1;
        var projectId = ProjectId;
        return Cache.RevalidateAsync(
            CacheKey(page, _pageSize, _selectedPolicy),
            () => Api.ServerTools.GetProjectArtifactsAsync(projectId, page, _pageSize, _selectedPolicy),
            result => { if (ProjectId == projectId) ApplyArtifacts(result); },
            _ => { },
            () => InvokeAsync(StateHasChanged));
    }

    private void OnRowClick(DataGridRowMouseEventArgs<PipelineArtifactDto> args)
    {
        if (args.Data is { } artifact)
            Nav.NavigateTo($"/artifacts/{artifact.Id}");
    }

    private async Task DownloadArtifact(PipelineArtifactDto artifact)
    {
        var stream = await Api.ServerTools.DownloadArtifactAsync(artifact.Id);
        if (stream is null) return;

        using var streamRef = new DotNetStreamReference(stream);
        await Js.InvokeVoidAsync("downloadFileFromStream", $"{artifact.Name}.zip", streamRef);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    private static BadgeStyle GetPolicyBadge(ArtifactRetentionPolicy policy) => policy switch
    {
        ArtifactRetentionPolicy.Build => BadgeStyle.Info,
        ArtifactRetentionPolicy.Deployed => BadgeStyle.Warning,
        ArtifactRetentionPolicy.Released => BadgeStyle.Success,
        _ => BadgeStyle.Light
    };

    private sealed record PolicyOption(string Label, ArtifactRetentionPolicy Value);
}
