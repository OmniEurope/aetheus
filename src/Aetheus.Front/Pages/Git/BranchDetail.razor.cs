// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Git;

public partial class BranchDetail
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private Layout.ProjectNavContextService ProjectNav { get; set; } = default!;

    [Parameter] public int BranchId { get; set; }

    private GitBranchDto? _branch;
    private int? _loadedBranchId;
    private bool _loading;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedBranchId == BranchId) return;
        _loadedBranchId = BranchId;
        var branchId = BranchId;
        _loading = true;
        GitBranchDto? branch;
        try { branch = await Api.GetGitBranchAsync(branchId); }
        catch (HttpRequestException) { branch = null; }
        if (BranchId != branchId) return;
        _branch = branch;
        if (_branch is not null)
            ProjectNav.Set(_branch.ProjectId);
        _loading = false;
    }

    /// <summary>External branch URL ({repo}/tree/{name}) when the repository has a secure HTTPS link.</summary>
    private string? BranchUrl
    {
        get
        {
            var repoUrl = _branch?.RepositoryUrl;
            if (string.IsNullOrEmpty(repoUrl) || _branch is null
                || !repoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return null;
            var baseUrl = repoUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repoUrl[..^4] : repoUrl;
            return $"{baseUrl.TrimEnd('/')}/tree/{_branch.Name}";
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
