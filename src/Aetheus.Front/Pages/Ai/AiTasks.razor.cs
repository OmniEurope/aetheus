// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Ai;

public partial class AiTasks : PagedDataGridPageBase<AiTaskDefinitionDto>, IDisposable
{
    [Inject] private NotifyHelper Notify { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private TaskTrackerService TaskTracker { get; set; } = default!;

    [Parameter] public int? ProjectId { get; set; }

    private List<AiTaskDefinitionDto> _tasks = [];
    private AiConsumptionDto? _consumption;
    private string _search = string.Empty;
    private int? _runningTaskId;

    protected override void OnInitialized()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        TaskTracker.OnTaskCompleted += OnTaskCompleted;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (ProjectId is { } projectId)
        {
            ProjectDetailDto? project;
            try { project = await Api.Projects.GetProjectDetailAsync(projectId); }
            catch (HttpRequestException) { project = null; }
            Breadcrumb.Set(
                new BreadcrumbItem(L["Projects"], "/projects"),
                new BreadcrumbItem(project?.Name ?? $"{L["Project"]} #{projectId}", $"/projects/{projectId}/overview"),
                new BreadcrumbItem(L["AiTasks"]));
        }
        else
        {
            Breadcrumb.Set(new BreadcrumbItem(L["AiTasks"]));
        }
        await LoadPageAsync(1, 25);
    }

    private Task ReloadAsync() => ReloadPageAsync();

    internal Task LoadDataAsync(LoadDataArgs args) => LoadPageFromArgsAsync(args);

    protected override async Task LoadPageAsync(int page, int pageSize)
    {
        var result = await LoadPageResultAsync(async () =>
        {
            var resultTask = Api.Ai.GetAiTasksAsync(
                page: page, pageSize: pageSize, search: _search, projectId: ProjectId);
            var consumptionTask = Api.Ai.GetAiConsumptionAsync(ProjectId);
            await Task.WhenAll(resultTask, consumptionTask);
            _consumption = consumptionTask.Result;
            return resultTask.Result;
        });
        _tasks = result.Items;
    }

    private Task OnSearchChangedAsync(object _) => ReloadAsync();

    private async Task OpenDialogAsync(AiTaskDefinitionDto? definition)
    {
        var result = await Dialog.OpenAsync<AiTaskDialog>(
            definition is null ? L["Create"] : L["Edit"],
            new Dictionary<string, object?>
            {
                ["DefinitionId"] = definition?.Id,
                ["FixedProjectId"] = ProjectId
            },
            new DialogOptions { Width = "780px", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });
        if (result is true) await ReloadAsync();
    }

    private async Task RunAsync(AiTaskDefinitionDto definition)
    {
        _runningTaskId = definition.Id;
        try
        {
            var started = await Api.Ai.RunAiTaskAsync(definition.Id);
            if (started is null) return;
            var completion = new TaskCompletionSource<TaskCompletedNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void CompleteMatchingRun(TaskCompletedNotification notification)
            {
                if (notification.TaskId == started.TaskId)
                    completion.TrySetResult(notification);
            }
            TaskTracker.OnTaskCompleted += CompleteMatchingRun;
            try
            {
                var result = await FindRunResultAsync(definition.Id, started.TaskId);
                if (result is null)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    try { await completion.Task.WaitAsync(timeout.Token); }
                    catch (OperationCanceledException) { }
                    result = await FindRunResultAsync(definition.Id, started.TaskId);
                }

                if (result is null)
                {
                    Notify.Info("Queued", "AiRunQueued");
                    return;
                }

                await Dialog.OpenAsync<AiResultDialog>(
                    L["AiRunResult"],
                    new Dictionary<string, object?> { ["InitialResult"] = result },
                    new DialogOptions { Width = "920px", Height = "80vh", AutoFocusFirstElement = false });
                await ReloadAsync();
            }
            finally
            {
                TaskTracker.OnTaskCompleted -= CompleteMatchingRun;
            }
        }
        finally
        {
            _runningTaskId = null;
        }
    }

    private async Task<AiRunResultDto?> FindRunResultAsync(int definitionId, int taskId)
    {
        var results = await Api.Ai.GetAiResultsAsync(definitionId: definitionId);
        return results.Items.FirstOrDefault(item => item.ServerTaskId == taskId);
    }

    private async Task OpenResultsAsync(AiTaskDefinitionDto definition)
    {
        await Dialog.OpenAsync<AiResultDialog>(
            $"{L["Results"]} - {definition.Name}",
            new Dictionary<string, object?> { ["DefinitionId"] = definition.Id },
            new DialogOptions { Width = "920px", Height = "80vh", AutoFocusFirstElement = false });
    }

    private async Task DeleteAsync(AiTaskDefinitionDto definition)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeleteAiTaskConfirm"], definition.Name), L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;
        await Ui.RunAsync(
            () => Api.Ai.DeleteAiTaskAsync(definition.Id),
            "Deleted",
            ReloadAsync,
            errorKey: "DeleteFailed",
            successTitleKey: "Deleted");
    }

    private static string FormatDuration(long durationMs) =>
        TimeSpan.FromMilliseconds(durationMs) is { } duration
            ? $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}"
            : "00:00";

    private bool CanCreate =>
        Permissions.CanWrite(ResourceType.Project)
        || Permissions.CanWrite(ResourceType.Server)
        || Permissions.GetPermissions().Any(permission =>
            permission.ResourceType is ResourceType.Project or ResourceType.Server
            && permission.Permission >= Permission.Write);

    private bool CanWrite(AiTaskDefinitionDto definition) =>
        definition.ProjectId is { } projectId
            ? Permissions.CanWrite(ResourceType.Project, projectId)
            : Permissions.CanWrite(ResourceType.Server, definition.ServerId);

    private bool CanAdmin(AiTaskDefinitionDto definition) =>
        definition.ProjectId is { } projectId
            ? Permissions.CanAdmin(ResourceType.Project, projectId)
            : Permissions.CanAdmin(ResourceType.Server, definition.ServerId);

    private void OnPermissionsChanged() => InvokeAsync(StateHasChanged);

    private void OnTaskCompleted(TaskCompletedNotification notification)
    {
        if (ShouldReloadFor(notification))
            _ = InvokeAsync(ReloadAsync);
    }

    internal static bool ShouldReloadFor(TaskCompletedNotification notification) =>
        notification.Operation == OperationKind.AiRun;

    public void Dispose()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        TaskTracker.OnTaskCompleted -= OnTaskCompleted;
    }
}
