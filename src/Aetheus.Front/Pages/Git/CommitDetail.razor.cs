// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Git;

public partial class CommitDetail
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private Layout.ProjectNavContextService ProjectNav { get; set; } = default!;

    [Parameter] public int CommitId { get; set; }

    private GitCommitDto? _commit;
    private int? _loadedCommitId;
    private bool _loading;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedCommitId == CommitId) return;
        _loadedCommitId = CommitId;
        var commitId = CommitId;
        _loading = true;
        GitCommitDto? commit;
        try { commit = await Api.GetGitCommitAsync(commitId); }
        catch (HttpRequestException) { commit = null; }
        if (CommitId != commitId) return;
        _commit = commit;
        if (_commit is not null)
            ProjectNav.Set(_commit.ProjectId);
        _loading = false;
    }

    /// <summary>External commit URL ({repo}/commit/{sha}) when the repository has a secure HTTPS link.</summary>
    private string? CommitUrl
    {
        get
        {
            var repoUrl = _commit?.RepositoryUrl;
            if (string.IsNullOrEmpty(repoUrl) || _commit is null
                || !repoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return null;
            var baseUrl = repoUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repoUrl[..^4] : repoUrl;
            return $"{baseUrl.TrimEnd('/')}/commit/{_commit.Sha}";
        }
    }

    private static string ShortSha(string sha) => sha[..Math.Min(8, sha.Length)];

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B"
    };
}
