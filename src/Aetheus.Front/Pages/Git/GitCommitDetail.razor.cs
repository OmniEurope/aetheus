// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http;
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Git;

public partial class GitCommitDetail
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    [Parameter] public int RepoId { get; set; }
    [Parameter] public string Sha { get; set; } = string.Empty;

    private GitLightCommitDetailDto? _detail;
    private bool _loading = true;
    private string? _loadedKey;

    protected override async Task OnParametersSetAsync()
    {
        var key = $"{RepoId}/{Sha}";
        if (_loadedKey == key) return;
        _loadedKey = key;

        _loading = true;
        Breadcrumb.Set(
            new BreadcrumbItem(L["GitRepositories"], "/git-repositories"),
            new BreadcrumbItem(L["CommitDetails"]));
        try
        {
            _detail = await Api.GetGitCommitDetailAsync(RepoId, Sha);
        }
        catch (HttpRequestException)
        {
            _detail = null; // 401 on expired JWT - redirect handled by AuthProvider
        }
        _loading = false;
    }

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;

    // Each diff block line becomes a <span> so the CSS can colour it by its git-diff marker.
    private static IEnumerable<string> SplitLines(string patch) =>
        patch.Replace("\r", string.Empty).Split('\n');

    private static string LineClass(string line)
    {
        if (line.StartsWith("@@", StringComparison.Ordinal)) return "diff-line diff-line-hunk";
        if (line.Length > 0 && line[0] == '+' && !line.StartsWith("+++", StringComparison.Ordinal)) return "diff-line diff-line-add";
        if (line.Length > 0 && line[0] == '-' && !line.StartsWith("---", StringComparison.Ordinal)) return "diff-line diff-line-del";
        return "diff-line";
    }

    private static BadgeStyle StatusBadge(string status) => status switch
    {
        "added" => BadgeStyle.Success,
        "deleted" => BadgeStyle.Danger,
        "renamed" => BadgeStyle.Info,
        _ => BadgeStyle.Light
    };

    private string LocalizeStatus(string status) => status.ToLowerInvariant() switch
    {
        "added" => L["Added"],
        "modified" => L["Modified"],
        "deleted" => L["Deleted"],
        "renamed" => L["Renamed"],
        "copied" => L["Copied"],
        _ => string.Format(L["GitStatusUnknown"], status)
    };
}
