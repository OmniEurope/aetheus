// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.DTOs.Organizations;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Organizations;

public partial class OrganizationDetail : IAsyncDisposable
{
    [Parameter] public int Id { get; set; }
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private NotifyHelper Notify { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;

    private OrganizationDetailDto? _org;
    private int? _loadedId;
    private bool _authorized;
    private bool _loading = true;
    private bool _loadError;
    // RT4M: reload the org (members/projects/general) when the backend broadcasts an Organization
    // change - member add/remove/role edits from any session refresh this page live.
    //
    // This bypasses the shared AdminEntitySubscription helper (used by the other admin list pages)
    // because that helper's callback drops the broadcast's entity id, so a page showing ONE org has
    // no way to tell a change to THIS org apart from a change to any other org - every Organization
    // broadcast anywhere would reload this page and blow away an unsaved edit. A direct hub
    // subscription lets the filter check the id before deciding to reload.
    private HubConnection? _hub;
    private List<UserDto> _users = [];
    private List<ProjectDto> _allProjects = [];
    private IEnumerable<int> _selectedProjectIds = [];
    private int? _newUserId;
    private OrganizationRole _newUserRole = OrganizationRole.Member;

    // General tab edit model.
    private readonly OrgGeneralModel _general = new();
    private bool _savingGeneral;

    private sealed class OrgGeneralModel
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 2)]
        [System.ComponentModel.DataAnnotations.RegularExpression("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", ErrorMessage = "SlugFormatError")]
        public string Slug { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.StringLength(500)]
        public string Description { get; set; } = string.Empty;
    }

    private List<RoleOption> _roleOptions => new()
    {
        new(OrganizationRole.Member, L["RoleMember"]),
        new(OrganizationRole.Maintainer, L["RoleMaintainer"]),
        new(OrganizationRole.Owner, L["RoleOwner"])
    };

    private record RoleOption(OrganizationRole Value, string Text);

    protected override async Task OnInitializedAsync()
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return;
        }
        _authorized = true;

        try
        {
            _hub = HubFactory.Create("admin");
            _hub.On<string, int, string>("AdminEntityChanged", (entity, id, _) =>
                entity == AdminEntities.Organization && id == Id
                    ? InvokeAsync(async () =>
                    {
                        // A dirty General/Projects tab means the user has an unsaved edit in
                        // progress: skip the reload (and the loading spinner) rather than
                        // clobbering it. The next explicit save or navigation resyncs.
                        if (IsGeneralTabDirty() || IsProjectsTabDirty()) return;
                        await LoadAsync();
                        StateHasChanged();
                    })
                    : Task.CompletedTask);
            await _hub.StartAsync();
        }
        catch { /* Hub unavailable - degrade to static */ }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!_authorized || _loadedId == Id) return;
        _loadedId = Id;
        _newUserId = null;
        await LoadAsync();
    }

    private bool IsGeneralTabDirty() =>
        _org is not null
        && (_general.Name != _org.Name || _general.Slug != _org.Slug || _general.Description != _org.Description);

    private bool IsProjectsTabDirty() =>
        _org is not null
        && !_selectedProjectIds.OrderBy(id => id).SequenceEqual(_org.Projects.Select(p => p.Id).OrderBy(id => id));

    private async Task LoadAsync()
    {
        var id = Id;
        _loading = true;
        _loadError = false;
        try
        {
            OrganizationDetailDto? organization;
            try { organization = await Api.GetOrganizationAsync(id); }
            catch (HttpRequestException)
            {
                if (Id == id)
                {
                    _org = null;
                    _loadError = true;
                }
                return;
            }
            if (Id != id) return;

            _org = organization;
            _selectedProjectIds = organization?.Projects.Select(p => p.Id).ToList() ?? [];
            if (organization is null)
            {
                _users = [];
                _allProjects = [];
                return;
            }

            var usersTask = LoadUsersOrEmptyAsync();
            var projectsTask = LoadProjectsOrEmptyAsync();
            await Task.WhenAll(usersTask, projectsTask);
            if (Id != id) return;
            _users = await usersTask;
            _allProjects = await projectsTask;

            _general.Name = organization.Name;
            _general.Slug = organization.Slug;
            _general.Description = organization.Description;
            Breadcrumb.Set(
                new BreadcrumbItem(L["Administration"], "/admin"),
                new BreadcrumbItem(L["Organizations"], "/admin/organizations"),
                new BreadcrumbItem(organization.Name));
        }
        finally
        {
            if (Id == id) _loading = false;
        }
    }

    private async Task<List<UserDto>> LoadUsersOrEmptyAsync()
    {
        try { return (await Api.GetUsersAsync(1, 100))?.Items ?? []; }
        catch (HttpRequestException) { return []; }
    }

    private async Task<List<ProjectDto>> LoadProjectsOrEmptyAsync()
    {
        try { return await Api.GetAllProjectsAsync(); }
        catch (HttpRequestException) { return []; }
    }

    private async Task OnSaveGeneral()
    {
        _savingGeneral = true;
        try
        {
            var updated = await Api.UpdateOrganizationAsync(Id, new UpdateOrganizationRequest
            {
                Name = _general.Name,
                Slug = _general.Slug,
                Description = _general.Description
            });
            if (updated is not null)
            {
                Notify.Success(L["Saved"]);
                await LoadAsync();
            }
        }
        finally
        {
            _savingGeneral = false;
        }
    }

    private async Task OnDeleteOrganization()
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["ConfirmDeleteOrganization"], _org?.Name ?? string.Empty),
            L["Delete"],
            new ConfirmOptions { OkButtonText = L["Delete"], CancelButtonText = L["Cancel"] });
        if (confirmed != true) return;
        if (await Api.DeleteOrganizationAsync(Id))
            Nav.NavigateTo("/admin/organizations");
    }

    private async Task OnAddMember()
    {
        if (_newUserId is null) return;
        var member = await Api.AddOrganizationMemberAsync(Id, new AddOrganizationMemberRequest
        {
            UserId = _newUserId.Value,
            Role = _newUserRole
        });
        if (member is not null && _org is not null)
        {
            _org.Members.Add(member);
            _newUserId = null;
            Notify.Success(L["MemberAdded"]);
        }
    }

    private async Task OnChangeMemberRole(OrganizationMemberDto member, OrganizationRole role)
    {
        var updated = await Api.UpdateOrganizationMemberAsync(Id, member.Id,
            new UpdateOrganizationMemberRequest { Role = role });
        if (updated is not null && _org is not null)
        {
            var idx = _org.Members.FindIndex(m => m.Id == member.Id);
            if (idx >= 0) _org.Members[idx] = updated;
            Notify.Success(L["Saved"]);
        }
    }

    private async Task OnRemoveMember(OrganizationMemberDto member)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["ConfirmRemoveMember"], member.Username),
            L["Remove"],
            new ConfirmOptions { OkButtonText = L["Remove"], CancelButtonText = L["Cancel"] });
        if (confirmed != true) return;
        if (await Api.RemoveOrganizationMemberAsync(Id, member.Id) && _org is not null)
            _org.Members.Remove(member);
    }

    private async Task OnSaveProjects()
    {
        var ok = await Api.AssignOrganizationProjectsAsync(Id,
            new AssignProjectsRequest { ProjectIds = _selectedProjectIds.ToList() });
        if (ok)
        {
            Notify.Success(L["Saved"]);
            await LoadAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }
}
