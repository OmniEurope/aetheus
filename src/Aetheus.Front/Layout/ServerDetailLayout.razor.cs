// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Servers;

namespace Aetheus.Front.Layout;

public partial class ServerDetailLayout : IDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;

    private bool _canWrite;
    private bool _deleteBusy;
    private bool _editVisible;
    private bool _editSaving;
    private string _editName = string.Empty;
    private ServerType _editType;
    private string _editTags = string.Empty;
    private static readonly ServerType[] _serverTypes = Enum.GetValues<ServerType>();

    private AgentUpdateProgressCard? _agentUpdateProgress;

    protected override void OnInitialized()
    {
        Loader.OnChanged += OnLoaderChanged;
        Nav.LocationChanged += OnLocationChanged;
        // MainLayout loads permissions in parallel with route activation, so a section page can
        // mount BEFORE Permissions.IsLoaded - capturing _canWrite=false would strand the
        // Contact/Update/Edit/Delete buttons as permanently disabled. Subscribe so they
        // reactivate the moment permissions land.
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();
    }

    private void OnLoaderChanged()
    {
        ReassertBreadcrumb();
        // The server id is only known once the loader resolves the detail; recompute the
        // resource-scoped permission then.
        RefreshCanWrite();
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e) =>
        ReassertBreadcrumb();

    private void ReassertBreadcrumb()
    {
        if (Loader.Server is null) return;
        var items = BreadcrumbRouteResolver.Resolve(Nav.ToBaseRelativePath(Nav.Uri), key => L[key]).ToArray();
        if (items.Length > 1)
            items[1] = items[1] with { Text = Loader.Server.Name, IsLoading = false };
        Breadcrumb.Set(items);
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        _ = InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() =>
        _canWrite = Permissions.CanWrite(ResourceType.Server, Loader.Server?.Id ?? 0);

    private Task OpenContactAgentDialog()
    {
        if (Loader.Server is null) return Task.CompletedTask;
        return Dialog.OpenAsync<ContactAgentDialog>(
            L["ContactAgent"],
            new Dictionary<string, object?> { { "ServerId", Loader.Server.Id }, { "ServerName", Loader.Server.Name } },
            new DialogOptions { Width = "480px", CloseDialogOnOverlayClick = false, AutoFocusFirstElement = false });
    }

    private async Task UpdateAgentAsync()
    {
        if (Loader.Server is null) return;
        var sourceVersion = Loader.Server.AgentVersion;
        var targetVersion = Loader.Server.AgentCompatibility?.TargetVersion
            ?? Loader.Server.AgentUpdateRequest?.TargetVersion
            ?? L["Unknown"].Value;
        var confirmed = await Dialog.Confirm(
            string.Format(L["UpdateAgentConfirm"], Loader.Server.Name, sourceVersion, targetVersion),
            L["UpdateAgent"],
            new ConfirmOptions { OkButtonText = L["UpdateAgent"], CancelButtonText = L["Cancel"] });
        if (confirmed != true) return;

        var result = await Api.Servers.UpdateAgentAsync(Loader.Server.Id);
        if (result?.Outcome == AgentUpdateQueueOutcome.AlreadyUpToDate)
            Toast.Info("UpdateAgent", "AgentAlreadyUpToDate", result.SourceVersion);
        else if (result is not null)
            Toast.Success("UpdateAgent", "UpdateAgentQueued", Loader.Server.Name);
        else
            Toast.Error("Error", "UpdateAgentFailed");
    }

    private async Task OnDeleteServer()
    {
        if (Loader.Server is null || _deleteBusy) return;
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeleteServerConfirm"], Loader.Server.Name),
            L["DeleteServer"],
            new ConfirmOptions { OkButtonText = L["Delete"], CancelButtonText = L["Cancel"] });
        if (confirmed != true) return;

        await DeleteServerConfirmedAsync();
    }

    internal async Task DeleteServerConfirmedAsync()
    {
        if (Loader.Server is null || _deleteBusy) return;

        _deleteBusy = true;
        StateHasChanged();
        try
        {
            var deleted = await Api.Servers.DeleteServerAsync(Loader.Server.Id);
            if (deleted)
            {
                Toast.Success(L["ServerDeleted"]);
                Nav.NavigateTo("/servers");
            }
            else
            {
                Toast.Error("Error", "DeleteFailed");
            }
        }
        catch (HttpRequestException)
        {
            Toast.Error("Error", "DeleteFailed");
        }
        catch (TaskCanceledException)
        {
            Toast.Error("Error", "DeleteFailed");
        }
        finally
        {
            _deleteBusy = false;
            StateHasChanged();
        }
    }

    private void OpenEditDialog()
    {
        if (Loader.Server is null) return;
        _editName = Loader.Server.Name;
        _editType = Loader.Server.Type;
        _editTags = string.Join(", ", Loader.Server.Tags);
        _editVisible = true;
    }

    private async Task SaveEditAsync()
    {
        if (Loader.Server is null) return;
        _editSaving = true;
        var tags = _editTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var request = new UpdateServerRequest
        {
            Name = _editName,
            Type = _editType,
            Tags = tags
        };
        var updated = await Api.Servers.UpdateServerAsync(Loader.Server.Id, request);
        if (updated is not null)
        {
            Loader.UpdateServerFields(updated.Name, updated.Type, updated.Tags);
            _editVisible = false;
            Toast.Success("Saved", "ServerUpdated");
        }
        else
        {
            Toast.Error("Error", "ServerUpdateFailed");
        }
        _editSaving = false;
    }

    public void Dispose()
    {
        Loader.OnChanged -= OnLoaderChanged;
        Nav.LocationChanged -= OnLocationChanged;
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
    }
}
