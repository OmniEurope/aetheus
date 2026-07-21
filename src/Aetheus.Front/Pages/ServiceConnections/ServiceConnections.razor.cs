// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.ServiceConnections;

public partial class ServiceConnections : IDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;

    private List<ServiceConnectionDto> _items = [];
    private int _count;
    private bool _loading;
    private string? _search;
    private bool _canWrite;
    private readonly HashSet<int> _testing = [];
    private readonly Dictionary<int, ServiceConnectionTestResultDto> _testResults = [];

    protected override async Task OnInitializedAsync()
    {
        Breadcrumb.Set(new BreadcrumbItem(L["ServiceConnections"]));
        // Permissions may land after the page mounts (MainLayout loads them in parallel);
        // subscribe so the create/edit/delete affordances reactivate the moment they arrive.
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();
        try { await LoadData(new LoadDataArgs()); }
        catch (HttpRequestException) { } // 401 on expired JWT - redirect handled by AuthProvider
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() => _canWrite = Permissions.CanWrite(ResourceType.ServiceConnection);

    private async Task LoadData(LoadDataArgs args)
    {
        _loading = true;
        var (page, pageSize) = args.ToPageRequest(20);
        var result = await Api.GetServiceConnectionsAsync(page, pageSize, _search);
        _items = result.Items;
        _count = result.TotalCount;
        _loading = false;
        StateHasChanged();
    }

    private async Task ReloadData()
    {
        try { await LoadData(new LoadDataArgs()); }
        catch (HttpRequestException) { }
    }

    private async Task TestAsync(int id)
    {
        _testing.Add(id);
        _testResults.Remove(id);
        StateHasChanged();
        try
        {
            var result = await Api.TestServiceConnectionAsync(id);
            // A missing connection (deleted meanwhile) is an honest "error", never a green.
            _testResults[id] = result ?? new ServiceConnectionTestResultDto
            {
                Status = ServiceConnectionTestStatus.Error,
                Message = L["ServiceConnectionNotFound"]
            };
        }
        catch (HttpRequestException)
        {
            _testResults[id] = new ServiceConnectionTestResultDto
            {
                Status = ServiceConnectionTestStatus.Error,
                Message = L["ServiceConnectionTestFailed"]
            };
        }
        finally
        {
            _testing.Remove(id);
            StateHasChanged();
        }
    }

    private Task OpenCreateDialogAsync() => OpenEditDialogAsync(null);

    private async Task OpenEditDialogAsync(ServiceConnectionDto? connection)
    {
        var result = await Dialog.OpenAsync<ServiceConnectionEditDialog>(
            connection is null ? L["Create"] : L["Edit"],
            new Dictionary<string, object?> { ["ConnectionId"] = connection?.Id },
            new DialogOptions { Width = "640px", CloseDialogOnOverlayClick = true });

        if (result is true)
        {
            _testResults.Clear(); // creds may have changed; drop stale badges
            await ReloadData();
        }
    }

    private async Task DeleteAsync(ServiceConnectionDto connection)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeleteServiceConnectionConfirm"], connection.Name), L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        await Ui.RunAsync(
            () => Api.DeleteServiceConnectionAsync(connection.Id),
            "Deleted",
            async () => { _testResults.Remove(connection.Id); await LoadData(new LoadDataArgs()); },
            errorKey: "DeleteFailed",
            successTitleKey: "Deleted");
    }

    private static BadgeStyle StatusBadge(ServiceConnectionTestStatus status) => status switch
    {
        ServiceConnectionTestStatus.Valid => BadgeStyle.Success,
        ServiceConnectionTestStatus.Invalid => BadgeStyle.Danger,
        ServiceConnectionTestStatus.Unsupported => BadgeStyle.Light,
        _ => BadgeStyle.Warning
    };

    public void Dispose() => Permissions.OnPermissionsChanged -= OnPermissionsChanged;
}
