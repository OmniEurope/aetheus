// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Tasks;

public partial class TaskDetail : IDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;

    [Parameter] public int TaskId { get; set; }

    private ServerTaskDto? _task;
    private List<TaskLogDto> _logs = [];
    private bool _loading;
    private int? _loadedTaskId;

    private bool CanCancel => _task is not null
        && _task.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Assigned or TaskExecutionStatus.Running
        && Permissions.CanWrite(ResourceType.Server, _task.ServerId);

    protected override void OnInitialized()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        Breadcrumb.Set(
            new BreadcrumbItem(L["Tasks"], "/tasks"),
            new BreadcrumbItem($"{L["Task"]} #{TaskId}"));
        if (_loadedTaskId == TaskId) return;

        _loadedTaskId = TaskId;
        _loading = true;
        _task = null;
        _logs = [];
        try
        {
            _task = await Api.Pipelines.GetTaskAsync(TaskId);
            if (_task is not null)
                _logs = await Api.Monitoring.GetTaskLogsAsync(TaskId);
        }
        catch (HttpRequestException)
        {
            _task = null;
            _logs = [];
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task CancelTaskAsync()
    {
        if (!CanCancel || _task is null) return;

        var confirmed = await Dialog.Confirm(
            string.Format(L["CancelTaskConfirm"], _task.Id, _task.Name),
            L["CancelTask"],
            new ConfirmOptions { OkButtonText = L["CancelTask"], CancelButtonText = L["Cancel"] });
        if (confirmed != true) return;

        var status = await Api.Pipelines.CancelTaskAsync(_task.Id);
        if (!status.Success)
        {
            Toast.Error("Error", "CancelTaskFailed");
            return;
        }

        Toast.Success("TaskCancelled", "TaskCancelledDetail", _task.Id);
        _task = await Api.Pipelines.GetTaskAsync(_task.Id);
    }

    private void OnPermissionsChanged() => InvokeAsync(StateHasChanged);

    private static string FormatDate(DateTime? value) => value?.ToString("g") ?? "-";

    public void Dispose()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
    }
}
