// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Tasks;

/// <summary>Modal body that renders a task's log lines. Opened via OmniDialogService from TaskListView.</summary>
public partial class TaskLogsDialog
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;

    [Parameter] public List<TaskLogDto>? Logs { get; set; }

    private void Close() => Dialog.Close();
}
