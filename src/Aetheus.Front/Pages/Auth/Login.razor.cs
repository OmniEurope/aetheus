// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Auth;

public partial class Login
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private LoginModel _model = new();
    private string? _error;
    private bool _loading;
    private bool _showPassword;
    private bool _totpRequired;
    private bool _useRecoveryCode;
    private bool _showForgotHint;
    private bool _isPublicDemo;

    private void ToggleShowPassword() => _showPassword = !_showPassword;

    private void ToggleForgotHint() => _showForgotHint = !_showForgotHint;

    private void ToggleRecoveryCode()
    {
        _useRecoveryCode = !_useRecoveryCode;
        if (_useRecoveryCode)
            _model.TotpCode = null;
        else
            _model.RecoveryCode = null;
    }

    protected override async Task OnInitializedAsync()
    {
        if (Auth.IsAuthenticated)
        {
            Nav.NavigateTo("/");
            return;
        }

        try
        {
            _isPublicDemo = (await Api.Auth.GetPublicDemoInfoAsync())?.Enabled == true;
        }
        catch (HttpRequestException)
        {
            _isPublicDemo = false;
        }
    }

    private async Task OnSubmit()
    {
        _loading = true;
        _error = null;

        try
        {
            var outcome = await Api.Auth.LoginAsync(new LoginRequest
            {
                Username = _model.Username,
                Password = _model.Password,
                RememberMe = _model.RememberMe,
                TotpCode = _totpRequired && !_useRecoveryCode ? _model.TotpCode : null,
                RecoveryCode = _totpRequired && _useRecoveryCode ? _model.RecoveryCode : null
            });

            if (outcome.Value is { } result)
            {
                // F-010: server signals TOTP is required - show the code input
                if (result.TotpRequired && string.IsNullOrEmpty(result.Token))
                {
                    _totpRequired = true;
                    return;
                }

                await Auth.LoginAsync(result.Token, result.RefreshToken);

                // Forced first-login password change: route straight to the mandatory screen and
                // skip the permissions prefetch (the user can't reach any gated page yet anyway).
                if (result.MustChangePassword)
                {
                    Nav.NavigateTo("/account/change-password");
                    return;
                }

                Nav.NavigateTo("/");
            }
            else
            {
                _error = outcome.StatusCode switch
                {
                    System.Net.HttpStatusCode.TooManyRequests => L["LoginRateLimited"].Value,
                    >= System.Net.HttpStatusCode.InternalServerError => L["LoginUnavailable"].Value,
                    _ => L["InvalidCredentials"].Value
                };
            }
        }
        catch (HttpRequestException)
        {
            _error = L["NetworkError"].Value;
        }
        finally
        {
            _loading = false;
        }
    }

    private class LoginModel
    {
        [Required]
        [StringLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }

        [StringLength(10)]
        public string? TotpCode { get; set; }

        [StringLength(20)]
        public string? RecoveryCode { get; set; }
    }
}
