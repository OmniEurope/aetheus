// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Git;

public partial class GitTagCreateDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int RepoId { get; set; }
    [Parameter] public List<GitLightBranchDto> Branches { get; set; } = [];

    private CreateGitLightTagRequest _request = new();
    private bool _submitting;

    private async Task Submit()
    {
        _submitting = true;
        var success = await Api.Git.CreateGitTagAsync(RepoId, _request);
        _submitting = false;

        if (success)
        {
            Toast.Success("Created", _request.Name);
            Dialog.Close(true);
        }
    }

    private void Cancel() => Dialog.Close(false);
}
