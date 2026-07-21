// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Tasks;

/// <summary>Modal body that renders a task's log lines. Opened via DialogService from TaskListView.</summary>
public partial class TaskLogsDialog
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter] public List<TaskLogDto>? Logs { get; set; }

    private void Close() => Dialog.Close();
}
