// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Pages.Auth;

public partial class Login
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private LoginModel _model = new();
    private string? _error;
    private bool _loading;
    private bool _showPassword;
    private bool _totpRequired;
    private bool _showForgotHint;

    private void ToggleShowPassword() => _showPassword = !_showPassword;

    private void ToggleForgotHint() => _showForgotHint = !_showForgotHint;

    protected override Task OnInitializedAsync()
    {
        if (Auth.IsAuthenticated)
            Nav.NavigateTo("/");
        return Task.CompletedTask;
    }

    private async Task OnSubmit()
    {
        _loading = true;
        _error = null;

        try
        {
            var result = await Api.LoginAsync(new LoginRequest
            {
                Username = _model.Username,
                Password = _model.Password,
                RememberMe = _model.RememberMe,
                TotpCode = _totpRequired ? _model.TotpCode : null,
                RecoveryCode = _totpRequired ? _model.RecoveryCode : null
            });

            if (result is not null)
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

                try
                {
                    var summary = await Api.GetMyPermissionsAsync();
                    if (summary is not null)
                        Permissions.SetPermissions(summary.EffectivePermissions, Auth.IsAdmin);
                }
                catch (Exception ex)
                {
                    // Permissions fetch failure should not block login
                    System.Diagnostics.Debug.WriteLine($"[Login] Permissions load failed: {ex.Message}");
                }
                Nav.NavigateTo("/");
            }
            else
            {
                _error = L["InvalidCredentials"].Value;
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
