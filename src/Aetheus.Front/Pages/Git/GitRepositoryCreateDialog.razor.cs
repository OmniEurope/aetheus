// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Git;

public partial class GitRepositoryCreateDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int ProjectId { get; set; }

    private CreateGitLightRepoRequest _request = new();
    private bool _submitting;

    protected override void OnInitialized()
    {
        _request = new CreateGitLightRepoRequest { ProjectId = ProjectId };
    }

    private async Task Submit()
    {
        _submitting = true;
        var result = await Api.CreateGitRepoAsync(_request);
        _submitting = false;

        if (result is not null)
        {
            Toast.Success("Created", result.Name);
            Dialog.Close(true);
        }
    }

    private void Cancel() => Dialog.Close(false);
}
