// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Git;

public partial class GitDefaultBranchButton
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int RepositoryId { get; set; }
    [Parameter, EditorRequired] public GitLightBranchDto Branch { get; set; } = default!;
    [Parameter] public bool CanWrite { get; set; }
    [Parameter] public EventCallback Changed { get; set; }

    private async Task SetDefaultBranchAsync()
    {
        if (!CanWrite || Branch.IsDefault) return;

        var updated = await Api.Git.UpdateGitRepoAsync(RepositoryId, new UpdateGitLightRepoRequest
        {
            DefaultBranch = Branch.Name
        });
        if (updated is null)
        {
            Toast.Error("Error", "SaveFailed");
            return;
        }

        Toast.Success(L["Saved"].Value, L["DefaultBranchUpdated"].Value);
        await Changed.InvokeAsync();
    }
}
