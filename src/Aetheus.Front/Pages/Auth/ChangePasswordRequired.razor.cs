// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Validation;

namespace Aetheus.Front.Pages.Auth;

public partial class ChangePasswordRequired
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private RealtimeSessionLifecycle RealtimeSession { get; set; } = default!;

    private readonly PasswordChangeModel _model = new();
    private bool _busy;
    private bool _showPasswords;
    private string? _error;

    private void ToggleShowPasswords() => _showPasswords = !_showPasswords;

    // FP3K: explicit sign-out for the forced-change screen (the user is otherwise trapped here).
    private async Task LogoutAsync()
    {
        await StopRealtimeAndLogoutAsync();
        Nav.NavigateTo("/login");
    }

    private async Task OnSubmit()
    {
        _busy = true;
        _error = null;

        var ok = await Api.Auth.ChangeOwnPasswordAsync(new ChangeUserPasswordRequest
        {
            CurrentPassword = _model.CurrentPassword,
            NewPassword = _model.NewPassword
        });

        if (ok)
        {
            // The change bumped the SecurityStamp (and cleared the forced-change flag), so the
            // current token is now stale. Log out and re-authenticate cleanly: the fresh token
            // no longer carries the must-change claim and the user lands on the app normally.
            Toast.Success("Saved", "PasswordChanged");
            await StopRealtimeAndLogoutAsync();
            Nav.NavigateTo("/login");
            return;
        }

        _error = L["PasswordChangeFailed"].Value;
        _busy = false;
    }

    private async Task StopRealtimeAndLogoutAsync()
    {
        await RealtimeSession.StopAsync();
        await Auth.LogoutAsync();
    }

}
