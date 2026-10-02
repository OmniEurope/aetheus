// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Notifications;

public partial class NotificationRuleEditDialog : EntityEditDialogBase
{
    [Parameter] public NotificationRuleDto? Rule { get; set; }
    [Parameter] public List<NotificationChannelDto> Channels { get; set; } = [];

    private bool IsEdit => Rule is not null;
    private EditModel _model = new();

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
        await RunBusyAsync(async () =>
        {
            var filter = string.IsNullOrWhiteSpace(_model.FilterJson) ? null : _model.FilterJson;
            if (IsEdit)
            {
                await Ui.RunAsync(
                    () => Api.Monitoring.UpdateNotificationRuleAsync(Rule!.Id, new UpdateNotificationRuleRequest
                    {
                        EventType = _model.EventType,
                        FilterJson = filter,
                        IsEnabled = _model.IsEnabled
                    }),
                    "Updated",
                    _ => CloseAfterSuccessAsync(),
                    successTitleKey: "Updated");
            }
            else
            {
                await Ui.RunAsync(
                    () => Api.Monitoring.CreateNotificationRuleAsync(new CreateNotificationRuleRequest
                    {
                        NotificationChannelId = _model.NotificationChannelId,
                        EventType = _model.EventType,
                        FilterJson = filter
                    }),
                    "Created",
                    _ => CloseAfterSuccessAsync(),
                    successTitleKey: "Created");
            }
        });
    }

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
