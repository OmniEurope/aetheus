// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Git;

public partial class GitRepositoryCreateDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
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
        var result = await Api.Git.CreateGitRepoAsync(_request);
        _submitting = false;

        if (result is not null)
        {
            Toast.Success("Created", result.Name);
            Dialog.Close(true);
        }
    }

    private void Cancel() => Dialog.Close(false);
}
