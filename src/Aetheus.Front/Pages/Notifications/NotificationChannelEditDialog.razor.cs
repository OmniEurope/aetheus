// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Notifications;

public partial class NotificationChannelEditDialog : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int? ChannelId { get; set; }

    private bool IsEdit => ChannelId is not null;
    private EditModel _model = new();
    private bool _busy;
    private bool _loading;

    private List<object> _types = [];

    protected override async Task OnInitializedAsync()
    {
        _types = Enum.GetValues<NotificationChannelType>()
            .Select(type => (object)new { Text = L.Localize(type), Value = type })
            .ToList();
        if (ChannelId is not int id) return;
        _loading = true;
        try
        {
            var channel = await Api.GetNotificationChannelAsync(id);
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
        catch (HttpRequestException) { }
        finally { _loading = false; }
    }

    private async Task SubmitAsync()
    {
        _busy = true;
        try
        {
            var config = string.IsNullOrWhiteSpace(_model.ConfigurationJson) ? "{}" : _model.ConfigurationJson;
            if (IsEdit)
            {
                await Ui.RunAsync(
                    () => Api.UpdateNotificationChannelAsync(ChannelId!.Value, new UpdateNotificationChannelRequest
                    {
                        Name = _model.Name,
                        ConfigurationJson = config,
                        IsEnabled = _model.IsEnabled
                    }),
                    "Updated",
                    _ => { Dialog.Close(true); return Task.CompletedTask; },
                    successTitleKey: "Updated");
            }
            else
            {
                await Ui.RunAsync(
                    () => Api.CreateNotificationChannelAsync(new CreateNotificationChannelRequest
                    {
                        Name = _model.Name,
                        Type = _model.Type,
                        ConfigurationJson = config
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
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public NotificationChannelType Type { get; set; } = NotificationChannelType.Slack;

        [StringLength(4000)]
        public string ConfigurationJson { get; set; } = "{}";

        public bool IsEnabled { get; set; } = true;
    }
}
