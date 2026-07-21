// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Git;

public partial class GitTagCreateDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int RepoId { get; set; }
    [Parameter] public List<GitLightBranchDto> Branches { get; set; } = [];

    private CreateGitLightTagRequest _request = new();
    private bool _submitting;

    private async Task Submit()
    {
        _submitting = true;
        var success = await Api.CreateGitTagAsync(RepoId, _request);
        _submitting = false;

        if (success)
        {
            Toast.Success("Created", _request.Name);
            Dialog.Close(true);
        }
    }

    private void Cancel() => Dialog.Close(false);
}
