// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Git;

public partial class GitPrCreateDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int RepoId { get; set; }
    [Parameter] public List<GitLightBranchDto> Branches { get; set; } = [];

    private CreateInternalPullRequestRequest _request = new();
    private bool _submitting;

    private async Task Submit()
    {
        _submitting = true;
        var result = await Api.CreateGitPullRequestAsync(RepoId, _request);
        _submitting = false;

        if (result is not null)
        {
            Toast.Success("Created", $"#{result.Number}: {result.Title}");
            Dialog.Close(true);
        }
    }

    private void Cancel() => Dialog.Close(false);
}
