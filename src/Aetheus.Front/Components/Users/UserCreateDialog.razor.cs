// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;

namespace Aetheus.Front.Components.Users;

public partial class UserCreateDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    // Optional seed for direct consumers. Production callers leave it empty so the picker uses
    // the paginated roles endpoint instead of receiving the complete role collection.
    [Parameter] public List<string> AvailableRoles { get; set; } = [];

    // Unambiguous set (no 0/O/1/l/I) so a generated password stays legible when revealed/copied.
    private const string PasswordCharset = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%*?";
    private const int GeneratedPasswordLength = 16;

    private readonly UserModel _model = new();
    private bool _busy;
    private bool _showPassword;
    private string? _error;

    private void ToggleShowPassword() => _showPassword = !_showPassword;

    private void GeneratePassword()
    {
        _model.Password = new string(RandomNumberGenerator.GetItems<char>(PasswordCharset, GeneratedPasswordLength));
        _showPassword = true;
        Toast.Info("GeneratePassword", "PasswordGenerated");
    }

    private async Task OnSubmit()
    {
        _busy = true;
        _error = null;

        try
        {
            var outcome = await Api.Auth.CreateUserAsync(new CreateUserRequest
            {
                Username = _model.Username,
                Password = _model.Password,
                Email = string.IsNullOrWhiteSpace(_model.Email) ? null : _model.Email,
                MustChangePassword = _model.MustChangePassword,
                Roles = _model.Roles
            });

            if (outcome.Value is { } created)
            {
                Toast.Success("Created", "UserCreated");
                Dialog.Close(created);
                return;
            }

            _error = string.IsNullOrWhiteSpace(outcome.Error?.Message)
                ? L["SaveFailed"].Value
                : outcome.Error.Message;
        }
        catch (HttpRequestException)
        {
            _error = L["SaveFailed"].Value;
            Toast.Error("Error", "SaveFailed");
        }
        _busy = false;
    }

    private void Cancel() => Dialog.Close(null);

    private sealed class UserModel
    {
        [Required(ErrorMessage = "UsernameRequired")]
        [StringLength(50, ErrorMessage = "UsernameTooLong")]
        public string Username { get; set; } = string.Empty;

        [StringLength(200)]
        [EmailAddress]
        public string? Email { get; set; }

        [Required(ErrorMessage = "PasswordRequired")]
        [StringLength(PasswordPolicy.MaximumLength, MinimumLength = PasswordPolicy.MinimumLength, ErrorMessage = "PasswordMinLength")]
        public string Password { get; set; } = string.Empty;

        public bool MustChangePassword { get; set; } = true;
        public List<string> Roles { get; set; } = [];
    }
}
