// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.DTOs.Organizations;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Users;

public partial class UserEdit
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    [Parameter] public int? Id { get; set; }

    private UserDto? _user;
    private UserModel _model = new();
    private bool _isNew => Id is null or 0;
    private bool _saving;
    private bool _loading;
    private PasswordChangeModel _passwordChange = new();
    private bool _changingPassword;
    private const int PasswordMinLength = PasswordPolicy.MinimumLength;
    private bool PasswordMeetsRules => _passwordChange.NewPassword.Length >= PasswordMinLength;
    private int? _previousId = int.MinValue;

    private List<string> _availableRoles = [];
    private List<EffectivePermissionDto>? _effectivePermissions;

    // Organizations tab (lazy-loaded on activation / deep-link).
    private List<UserOrganizationDto> _userOrgs = [];
    private List<OrganizationDto> _availableOrgs = [];
    private bool _orgsLoaded;
    private bool _loadingOrgs;
    private bool _addingOrg;
    private int? _selectedOrgId;
    private OrganizationRole _newOrgRole = OrganizationRole.Member;

    private List<OrgRoleOption> _orgRoleOptions => new()
    {
        new(OrganizationRole.Member, L["RoleMember"]),
        new(OrganizationRole.Maintainer, L["RoleMaintainer"]),
        new(OrganizationRole.Owner, L["RoleOwner"])
    };

    private record OrgRoleOption(OrganizationRole Value, string Text);

    protected override async Task OnParametersSetAsync()
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return;
        }

        if (Id == _previousId) return;
        _previousId = Id;

        try
        {
            _availableRoles = await Api.GetRolesAsync();

            if (!_isNew)
            {
                _loading = true;
                _user = await Api.GetUserDetailAsync(Id!.Value);
                if (_user is not null)
                {
                    _model = new UserModel
                    {
                        Username = _user.Username,
                        Email = _user.Email,
                        IsActive = _user.IsActive,
                        MustChangePassword = _user.MustChangePassword,
                        Roles = _user.Roles.ToList()
                    };
                }
                _loading = false;
            }
        }
        catch (HttpRequestException) { _loading = false; } // 401 on expired JWT - redirect handled by AuthProvider

        if (!_isNew)
            await LoadEffectivePermissions();

        Breadcrumb.Set(
            new BreadcrumbItem(L["Users"], "/users"),
            new BreadcrumbItem(_isNew ? L["NewUser"] : _user?.Username ?? L["User"]));

        // Deep-link straight to the organizations tab: load it up front.
        if (!_isNew && string.Equals(CurrentTab(), "organizations", StringComparison.OrdinalIgnoreCase))
            await LoadUserOrgsAsync();
    }

    private string? CurrentTab()
    {
        var query = QueryHelpers.ParseQuery(new Uri(Nav.Uri).Query);
        return query.TryGetValue("tab", out var value) ? value.ToString() : null;
    }

    // Slugs order: 0 general, 1 roles, 2 organizations, 3 security. Load the orgs tab on activation.
    private async Task OnTabChanged(int index)
    {
        if (index == 2 && !_orgsLoaded)
            await LoadUserOrgsAsync();
    }

    private async Task LoadUserOrgsAsync()
    {
        if (_isNew || Id is null or 0) return;
        _loadingOrgs = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            _userOrgs = await Api.GetUserOrganizationsAsync(Id.Value);
            var memberOf = _userOrgs.Select(o => o.OrganizationId).ToHashSet();
            var available = new List<OrganizationDto>();
            const int pageSize = 200;
            for (var page = 1; ; page++)
            {
                var result = await Api.GetOrganizationsAsync(null, page, pageSize);
                if (result is null) break;
                available.AddRange(result.Items.Where(o => !memberOf.Contains(o.Id)));
                if (page * pageSize >= result.TotalCount || result.Items.Count == 0) break;
            }
            _availableOrgs = available;
            _orgsLoaded = true;
        }
        finally
        {
            _loadingOrgs = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task OnAddOrg()
    {
        if (_selectedOrgId is null || Id is null) return;
        _addingOrg = true;
        try
        {
            var member = await Api.AddOrganizationMemberAsync(_selectedOrgId.Value,
                new AddOrganizationMemberRequest { UserId = Id.Value, Role = _newOrgRole });
            if (member is not null)
            {
                Toast.Success("Saved", "Saved");
                _selectedOrgId = null;
                _newOrgRole = OrganizationRole.Member;
                await LoadUserOrgsAsync();
            }
        }
        finally
        {
            _addingOrg = false;
        }
    }

    private async Task OnChangeOrgRole(UserOrganizationDto org, OrganizationRole role)
    {
        var updated = await Api.UpdateOrganizationMemberAsync(org.OrganizationId, org.MemberId,
            new UpdateOrganizationMemberRequest { Role = role });
        if (updated is not null)
        {
            var idx = _userOrgs.FindIndex(o => o.MemberId == org.MemberId);
            if (idx >= 0) _userOrgs[idx] = org with { Role = role };
            Toast.Success("Saved", "Saved");
        }
    }

    private async Task OnRemoveOrg(UserOrganizationDto org)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["ConfirmRemoveFromOrganization"].Value, org.Name),
            L["Remove"].Value,
            new ConfirmOptions { OkButtonText = L["Remove"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        if (await Api.RemoveOrganizationMemberAsync(org.OrganizationId, org.MemberId))
            await LoadUserOrgsAsync();
    }

    private async Task OnSubmit()
    {
        _saving = true;
        if (_isNew)
        {
            if (string.IsNullOrWhiteSpace(_model.Password) || _model.Password.Length < PasswordPolicy.MinimumLength)
            {
                Toast.Error(L["Error"], L["PasswordMinLength"]);
                _saving = false;
                return;
            }

            var created = await Api.CreateUserAsync(new CreateUserRequest
            {
                Username = _model.Username,
                Password = _model.Password,
                Email = string.IsNullOrWhiteSpace(_model.Email) ? null : _model.Email,
                MustChangePassword = _model.MustChangePassword,
                Roles = _model.Roles
            });
            if (created is not null)
            {
                Toast.Success("Created", "UserCreated");
                Nav.NavigateTo($"/users/{created.Id}");
            }
        }
        else
        {
            var updated = await Api.UpdateUserAsync(Id!.Value, new UpdateUserRequest
            {
                Username = _model.Username,
                Email = string.IsNullOrWhiteSpace(_model.Email) ? null : _model.Email,
                IsActive = _model.IsActive,
                MustChangePassword = _model.MustChangePassword,
                Roles = _model.Roles
            });
            if (updated is not null)
            {
                _user = await Api.GetUserDetailAsync(Id!.Value);
                await LoadEffectivePermissions();
                Toast.Success("Saved", "UserSaved");
            }
        }
        _saving = false;
    }

    private async Task OnDelete()
    {
        var confirmed = await Dialog.Confirm(L["DeleteUserConfirm"].Value, L["DeleteUser"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed == true)
        {
            var status = await Api.DeleteUserAsync(Id!.Value);
            if (status.Success)
                Nav.NavigateTo("/users");
            else
                Toast.Error("Error", "DeleteFailed");
        }
    }

    private async Task OnChangePassword(PasswordChangeModel model)
    {
        // The button is always clickable; validate here and tell the user exactly why it failed
        // instead of silently doing nothing.
        if (!PasswordMeetsRules)
        {
            Toast.Warning("Error", "PasswordMinLength");
            return;
        }

        _changingPassword = true;
        try
        {
            var status = await Api.ChangeUserPasswordAsync(Id!.Value, new ChangeUserPasswordRequest { NewPassword = model.NewPassword });
            if (status.Success)
            {
                Toast.Success("Saved", "PasswordChanged");
                _passwordChange = new PasswordChangeModel();
            }
            else
            {
                Toast.Error("Error", "PasswordChangeFailed");
            }
        }
        finally
        {
            _changingPassword = false;
        }
    }

    private void OnRoleToggled(string role, bool isChecked)
    {
        if (isChecked && !_model.Roles.Contains(role))
            _model.Roles.Add(role);
        else if (!isChecked)
            _model.Roles.Remove(role);
    }

    private async Task LoadEffectivePermissions()
    {
        if (_isNew || Id is null or 0) return;

        var summary = await Api.GetUserEffectivePermissionsAsync(Id!.Value);
        _effectivePermissions = summary?.EffectivePermissions ?? [];
    }

    private static BadgeStyle GetPermissionBadgeStyle(Permission permission) => permission switch
    {
        Permission.Admin => BadgeStyle.Danger,
        Permission.Write => BadgeStyle.Warning,
        _ => BadgeStyle.Info
    };

    private class UserModel
    {
        [Required(ErrorMessage = "UsernameRequired")]
        [StringLength(50, ErrorMessage = "UsernameTooLong")]
        public string Username { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Email { get; set; }

        [StringLength(100)]
        public string Password { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public bool MustChangePassword { get; set; } = true;
        public List<string> Roles { get; set; } = [];
    }

    private sealed class PasswordChangeModel
    {
        [Required, StringLength(100, MinimumLength = PasswordMinLength)]
        public string NewPassword { get; set; } = string.Empty;
    }
}
