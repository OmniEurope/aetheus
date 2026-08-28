// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Ai;

public partial class AiRunnerProfiles : PagedDataGridPageBase<AiRunnerProfileDto>
{
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;

    private List<AiRunnerProfileDto> _profiles = [];

    protected override async Task OnInitializedAsync()
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return;
        }
        Breadcrumb.Set(
            new BreadcrumbItem(L["Administration"], "/admin"),
            new BreadcrumbItem(L["AiRunnerProfiles"]));
        await LoadPageAsync(1, 25);
    }

    internal Task LoadDataAsync(LoadDataArgs args) => LoadPageFromArgsAsync(args);

    protected override async Task LoadPageAsync(int page, int pageSize)
    {
        var result = await LoadPageResultAsync(
            () => Api.Ai.GetAiRunnerProfilesAsync(page, pageSize));
        _profiles = result.Items;
    }

    private async Task OpenDialogAsync(AiRunnerProfileDto? profile)
    {
        var result = await Dialog.OpenAsync<AiRunnerProfileDialog>(
            profile is null ? L["Create"] : L["Edit"],
            new Dictionary<string, object?> { ["ProfileId"] = profile?.Id },
            new DialogOptions { Width = "760px", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });
        if (result is true) await ReloadPageAsync();
    }

    private async Task DeleteAsync(AiRunnerProfileDto profile)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeleteAiRunnerProfileConfirm"], profile.Name), L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;
        await Ui.RunAsync(
            () => Api.Ai.DeleteAiRunnerProfileAsync(profile.Id),
            "Deleted",
            ReloadPageAsync,
            errorKey: "DeleteFailed",
            successTitleKey: "Deleted");
    }
}
