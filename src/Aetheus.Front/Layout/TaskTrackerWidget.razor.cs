// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Layout;

public partial class TaskTrackerWidget : IDisposable
{
    [Inject] private TaskTrackerService Tracker { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private bool _popoverOpen;
    private int _count;

    protected override void OnInitialized()
    {
        Tracker.OnChanged += OnTrackerChanged;
        _count = Tracker.Count;
    }

    private void OnTrackerChanged()
    {
        _count = Tracker.Count;
        _ = InvokeAsync(StateHasChanged);
    }

    private void TogglePopover() => _popoverOpen = !_popoverOpen;

    // A Pending/Assigned task whose server agent is offline cannot progress on its own - surface that
    // instead of an opaque "Pending". Mirrors Tasks.IsStalledOnOfflineAgent.
    private static bool IsStalledOnOfflineAgent(ServerTaskDto task) =>
        task.ServerStatus == ServerStatus.Offline
        && task.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Assigned;

    private static string StatusIcon(TaskExecutionStatus status) => status switch
    {
        TaskExecutionStatus.Running => "play_arrow",
        TaskExecutionStatus.Assigned => "schedule",
        TaskExecutionStatus.Pending => "hourglass_top",
        _ => "task_alt"
    };

    private static string StatusIconClass(TaskExecutionStatus status) => status switch
    {
        TaskExecutionStatus.Running => "task-tracker-icon-running",
        TaskExecutionStatus.Assigned => "task-tracker-icon-assigned",
        _ => "task-tracker-icon-pending"
    };

    public void Dispose()
    {
        Tracker.OnChanged -= OnTrackerChanged;
    }
}
