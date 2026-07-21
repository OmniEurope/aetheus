// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectBoardSection : ComponentBase, IDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;

    [Parameter] public int ProjectId { get; set; }

    private static readonly WorkItemStatus[] Columns =
        [WorkItemStatus.New, WorkItemStatus.Active, WorkItemStatus.Resolved, WorkItemStatus.Closed, WorkItemStatus.Removed];

    private List<WorkItemDto> _items = [];
    private bool _loading = true;
    private bool _canWrite;
    private WorkItemDto? _dragged;
    private WorkItemStatus? _dropTarget;

    private string? _assigneeFilter;
    private string? _tagFilter;

    private List<string> Assignees => _items
        .Where(i => !string.IsNullOrEmpty(i.AssigneeName))
        .Select(i => i.AssigneeName!)
        .Distinct().OrderBy(n => n).ToList();

    private List<string> Tags => _items
        .SelectMany(i => i.Tags)
        .Distinct().OrderBy(t => t).ToList();

    protected override void OnInitialized()
    {
        // Permissions may land after the section mounts (MainLayout loads them in parallel);
        // subscribe so the drag-and-drop + create/edit/delete affordances reactivate on arrival.
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() => _canWrite = Permissions.CanWrite(ResourceType.Project);

    protected override async Task OnParametersSetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var board = await Api.GetWorkItemBoardAsync(ProjectId);
            _items = board.SelectMany(c => c.Items).ToList();
        }
        catch (HttpRequestException) { _items = []; }
        finally { _loading = false; }
    }

    private IEnumerable<WorkItemDto> ColumnItems(WorkItemStatus status) => _items
        .Where(i => i.Status == status)
        .Where(i => _assigneeFilter is null || i.AssigneeName == _assigneeFilter)
        .Where(i => _tagFilter is null || i.Tags.Contains(_tagFilter))
        .OrderBy(i => i.Order).ThenByDescending(i => i.Priority);

    private void OnDragStart(WorkItemDto item)
    {
        if (_canWrite) _dragged = item;
    }

    private void OnDragEnd()
    {
        _dragged = null;
        _dropTarget = null;
    }

    private async Task OnDropAsync(WorkItemStatus status)
    {
        var item = _dragged;
        _dropTarget = null;
        _dragged = null;
        if (item is null || !_canWrite || item.Status == status) return;

        // Optimistic local move; the target order is one past the current max in that column.
        var order = _items.Where(i => i.Status == status).Select(i => i.Order).DefaultIfEmpty(-1).Max() + 1;
        var idx = _items.FindIndex(i => i.Id == item.Id);
        if (idx >= 0) _items[idx] = item with { Status = status, Order = order };
        StateHasChanged();

        var result = await Api.MoveWorkItemAsync(item.Id, new MoveWorkItemRequest { Status = status, Order = order });
        if (result is null)
        {
            // Persist failed: reload from the server so the board never shows an unpersisted move.
            Toast.Error("Error", "SaveFailed");
            await LoadAsync();
        }
    }

    private async Task OpenCreateAsync() => await OpenEditAsync(null);

    private async Task OpenEditAsync(WorkItemDto? item)
    {
        var result = await Dialog.OpenAsync<WorkItemEditDialog>(
            item is null ? L["Create"] : L["Edit"],
            new Dictionary<string, object?> { ["ProjectId"] = ProjectId, ["Item"] = item },
            new DialogOptions { Width = "640px", CloseDialogOnOverlayClick = true });
        if (result is true) await LoadAsync();
    }

    private async Task DeleteAsync(WorkItemDto item)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeleteWorkItemConfirm"], item.Title), L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var status = await Api.DeleteWorkItemAsync(item.Id);
        if (status.Success) { _items.RemoveAll(i => i.Id == item.Id); StateHasChanged(); }
        else Toast.Error("Error", "DeleteFailed");
    }

    private static BadgeStyle TypeBadge(WorkItemType type) => type switch
    {
        WorkItemType.Bug => BadgeStyle.Danger,
        WorkItemType.Feature => BadgeStyle.Success,
        WorkItemType.Epic => BadgeStyle.Info,
        _ => BadgeStyle.Light
    };

    public void Dispose() => Permissions.OnPermissionsChanged -= OnPermissionsChanged;
}
