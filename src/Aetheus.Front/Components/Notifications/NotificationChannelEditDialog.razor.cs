// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Notifications;

public partial class NotificationChannelEditDialog : EntityEditDialogBase
{
    [Parameter] public int? ChannelId { get; set; }

    private bool IsEdit => ChannelId is not null;
    private EditModel _model = new();
    private bool _loading;

    private List<OmniOption<NotificationChannelType>> _types = [];

    protected override async Task OnInitializedAsync()
    {
        _types = Enum.GetValues<NotificationChannelType>()
            .Select(type => new OmniOption<NotificationChannelType>(type, L.Localize(type)))
            .ToList();
        if (ChannelId is not int id) return;
        _loading = true;
        try
        {
            var channel = await Api.Monitoring.GetNotificationChannelAsync(id);
            if (channel is not null)
            {
                _model = new EditModel
                {
                    Name = channel.Name,
                    Type = channel.Type,
                    ConfigurationJson = channel.ConfigurationJson,
                    IsEnabled = channel.IsEnabled
                };
            }
        }
        catch (HttpRequestException) { } // 401 on expired JWT - redirect handled by AuthProvider
        finally { _loading = false; }
    }

    private async Task SubmitAsync()
    {
        await RunBusyAsync(async () =>
        {
            var config = string.IsNullOrWhiteSpace(_model.ConfigurationJson) ? "{}" : _model.ConfigurationJson;
            if (IsEdit)
            {
                await Ui.RunAsync(
                    () => Api.Monitoring.UpdateNotificationChannelAsync(ChannelId!.Value, new UpdateNotificationChannelRequest
                    {
                        Name = _model.Name,
                        ConfigurationJson = config,
                        IsEnabled = _model.IsEnabled
                    }),
                    "Updated",
                    _ => CloseAfterSuccessAsync(),
                    successTitleKey: "Updated");
            }
            else
            {
                await Ui.RunAsync(
                    () => Api.Monitoring.CreateNotificationChannelAsync(new CreateNotificationChannelRequest
                    {
                        Name = _model.Name,
                        Type = _model.Type,
                        ConfigurationJson = config
                    }),
                    "Created",
                    _ => CloseAfterSuccessAsync(),
                    successTitleKey: "Created");
            }
        });
    }

    private sealed class EditModel
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public NotificationChannelType Type { get; set; } = NotificationChannelType.Slack;

        [StringLength(4000)]
        public string ConfigurationJson { get; set; } = "{}";

        public bool IsEnabled { get; set; } = true;
    }
}
