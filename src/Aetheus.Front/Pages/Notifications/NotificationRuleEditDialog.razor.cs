// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Notifications;

public partial class NotificationRuleEditDialog : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public NotificationRuleDto? Rule { get; set; }
    [Parameter] public List<NotificationChannelDto> Channels { get; set; } = [];

    private bool IsEdit => Rule is not null;
    private EditModel _model = new();
    private bool _busy;

    protected override void OnInitialized()
    {
        if (Rule is { } r)
        {
            _model = new EditModel
            {
                NotificationChannelId = r.NotificationChannelId,
                EventType = r.EventType,
                FilterJson = r.FilterJson,
                IsEnabled = r.IsEnabled
            };
        }
        else if (Channels.Count > 0)
        {
            _model.NotificationChannelId = Channels[0].Id;
        }
    }

    private async Task SubmitAsync()
    {
        _busy = true;
        try
        {
            var filter = string.IsNullOrWhiteSpace(_model.FilterJson) ? null : _model.FilterJson;
            if (IsEdit)
            {
                await Ui.RunAsync(
                    () => Api.UpdateNotificationRuleAsync(Rule!.Id, new UpdateNotificationRuleRequest
                    {
                        EventType = _model.EventType,
                        FilterJson = filter,
                        IsEnabled = _model.IsEnabled
                    }),
                    "Updated",
                    _ => { Dialog.Close(true); return Task.CompletedTask; },
                    successTitleKey: "Updated");
            }
            else
            {
                await Ui.RunAsync(
                    () => Api.CreateNotificationRuleAsync(new CreateNotificationRuleRequest
                    {
                        NotificationChannelId = _model.NotificationChannelId,
                        EventType = _model.EventType,
                        FilterJson = filter
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
        public int NotificationChannelId { get; set; }

        [Required]
        [StringLength(100)]
        public string EventType { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? FilterJson { get; set; }

        public bool IsEnabled { get; set; } = true;
    }
}
