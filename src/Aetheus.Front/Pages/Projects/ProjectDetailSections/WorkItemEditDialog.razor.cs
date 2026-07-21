// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class WorkItemEditDialog : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int ProjectId { get; set; }
    [Parameter] public WorkItemDto? Item { get; set; }

    private bool IsEdit => Item is not null;
    private EditModel _model = new();
    private bool _busy;

    private static readonly List<object> _types = Enum.GetValues<WorkItemType>()
        .Select(t => (object)new { Text = t.ToString(), Value = t }).ToList();

    private static readonly List<object> _statuses = Enum.GetValues<WorkItemStatus>()
        .Select(s => (object)new { Text = s.ToString(), Value = s }).ToList();

    protected override void OnInitialized()
    {
        if (Item is { } i)
        {
            _model = new EditModel
            {
                Title = i.Title,
                Type = i.Type,
                Status = i.Status,
                Priority = i.Priority,
                Description = i.Description,
                TagsCsv = string.Join(", ", i.Tags)
            };
        }
    }

    private async Task SubmitAsync()
    {
        _busy = true;
        try
        {
            var tags = (_model.TagsCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct().ToList();

            if (IsEdit)
            {
                await Ui.RunAsync(
                    () => Api.UpdateWorkItemAsync(Item!.Id, new UpdateWorkItemRequest
                    {
                        Type = _model.Type,
                        Title = _model.Title,
                        Description = _model.Description,
                        Status = _model.Status,
                        AssigneeUserId = Item.AssigneeUserId,
                        ParentId = Item.ParentId,
                        Priority = _model.Priority,
                        Order = Item.Order,
                        Tags = tags,
                        LinkedPipelineRunId = Item.LinkedPipelineRunId
                    }),
                    "Updated",
                    _ => { Dialog.Close(true); return Task.CompletedTask; },
                    successTitleKey: "Updated");
            }
            else
            {
                await Ui.RunAsync(
                    () => Api.CreateWorkItemAsync(new CreateWorkItemRequest
                    {
                        ProjectId = ProjectId,
                        Type = _model.Type,
                        Title = _model.Title,
                        Description = _model.Description,
                        Status = _model.Status,
                        Priority = _model.Priority,
                        Tags = tags
                    }),
                    "Created",
                    _ => { Dialog.Close(true); return Task.CompletedTask; },
                    successTitleKey: "Created");
            }
        }
        finally { _busy = false; }
    }

    private void Cancel() => Dialog.Close(false);

    private sealed class EditModel
    {
        [Required]
        [StringLength(300)]
        public string Title { get; set; } = string.Empty;

        public WorkItemType Type { get; set; } = WorkItemType.Task;
        public WorkItemStatus Status { get; set; } = WorkItemStatus.New;
        public int Priority { get; set; }

        [StringLength(4000)]
        public string? Description { get; set; }

        public string? TagsCsv { get; set; }
    }
}
