// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Pages.Artifacts;

public partial class ArtifactDetail
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private Layout.ProjectNavContextService ProjectNav { get; set; } = default!;

    [Parameter] public int ArtifactId { get; set; }

    private PipelineArtifactDto? _artifact;
    private int? _loadedArtifactId;
    private bool _loading;
    private bool _downloading;

    // The Distribution panel groups where the artifact went (releases / environment / project).
    // Hide it entirely when the artifact has none of those, so we never render an empty panel.
    private bool HasDistribution =>
        _artifact is not null &&
        (_artifact.Releases.Count > 0 || !string.IsNullOrEmpty(_artifact.EnvironmentName) || _artifact.ProjectId.HasValue);

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedArtifactId == ArtifactId) return;
        _loadedArtifactId = ArtifactId;
        var artifactId = ArtifactId;
        _loading = true;
        PipelineArtifactDto? artifact;
        try { artifact = await Api.GetArtifactAsync(artifactId); }
        catch (HttpRequestException) { artifact = null; }
        if (ArtifactId != artifactId) return;
        _artifact = artifact;
        if (_artifact?.ProjectId is not null)
            ProjectNav.Set(_artifact.ProjectId.Value);
        _loading = false;
    }

    private async Task DownloadAsync()
    {
        if (_artifact is null || _downloading) return;
        _downloading = true;
        StateHasChanged();
        try
        {
            var stream = await Api.DownloadArtifactAsync(_artifact.Id);
            if (stream is not null)
            {
                using var streamRef = new DotNetStreamReference(stream);
                await Js.InvokeVoidAsync("downloadFileFromStream", $"{_artifact.Name}.zip", streamRef);
            }
        }
        finally
        {
            _downloading = false;
            StateHasChanged();
        }
    }

    private Task CopyPathAsync() =>
        _artifact is null ? Task.CompletedTask : Clipboard.CopyAsync(_artifact.FilePath, _artifact.FilePath);

    private Task CopyChecksumAsync() =>
        string.IsNullOrEmpty(_artifact?.Sha256) ? Task.CompletedTask : Clipboard.CopyAsync(_artifact.Sha256, _artifact.Sha256);

    private static BadgeStyle GetRetentionBadge(ArtifactRetentionPolicy policy) => policy switch
    {
        ArtifactRetentionPolicy.Released => BadgeStyle.Success,
        ArtifactRetentionPolicy.Deployed => BadgeStyle.Warning,
        _ => BadgeStyle.Light
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B"
    };

    private static string ShortSha(string commitHash) => commitHash[..Math.Min(8, commitHash.Length)];
}
