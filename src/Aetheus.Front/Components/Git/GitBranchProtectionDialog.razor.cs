// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Git;

public partial class GitBranchProtectionDialog
{
    [Parameter] public int RepoId { get; set; }

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private string _pattern = string.Empty;
    private bool _preventDeletion = true;
    private bool _preventForcePush = true;

    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(_pattern))
        {
            Toast.Error(L["Required"].Value, L["Pattern"].Value);
            return;
        }

        var request = new CreateBranchProtectionRuleRequest
        {
            Pattern = _pattern.Trim(),
            PreventDeletion = _preventDeletion,
            PreventForcePush = _preventForcePush,
            // Recette R2-042: set aside, never enforced; the request keeps the field for the code kept.
            RequirePullRequest = false
        };

        var result = await Api.Git.CreateGitBranchProtectionRuleAsync(RepoId, request);
        if (result is not null)
        {
            Toast.Success(L["Saved"].Value, _pattern);
            Dialog.Close(true);
        }
    }
}
