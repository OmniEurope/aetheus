// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.ServiceConnections;

public partial class ServiceConnectionEditDialog : ComponentBase
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    /// <summary>The connection being edited; <c>null</c> opens the dialog in create mode.</summary>
    [Parameter] public int? ConnectionId { get; set; }

    private bool IsEdit => ConnectionId is not null;
    private EditModel _model = new();
    private bool _busy;
    private bool _loading;

    private List<object> _types = [];

    protected override async Task OnInitializedAsync()
    {
        _types = Enum.GetValues<ServiceConnectionType>()
            .Select(type => (object)new { Text = L.Localize(type), Value = type })
            .ToList();
        if (ConnectionId is not int id) return;

        _loading = true;
        try
        {
            var detail = await Api.GetServiceConnectionAsync(id);
            if (detail is not null)
            {
                _model = new EditModel
                {
                    Name = detail.Name,
                    Description = detail.Description,
                    Type = detail.Type,
                    Url = detail.Url,
                    ConfigurationJson = detail.ConfigurationJson,
                    ProjectId = detail.ProjectId
                };
            }
        }
        catch (HttpRequestException) { /* 401 redirect handled upstream */ }
        finally { _loading = false; }
    }

    private async Task SubmitAsync()
    {
        _busy = true;
        try
        {
            if (IsEdit)
            {
                await Ui.RunAsync(
                    () => Api.UpdateServiceConnectionAsync(ConnectionId!.Value, new UpdateServiceConnectionRequest
                    {
                        Name = _model.Name,
                        Description = _model.Description,
                        ProjectId = _model.ProjectId,
                        Url = _model.Url,
                        ConfigurationJson = string.IsNullOrWhiteSpace(_model.ConfigurationJson) ? "{}" : _model.ConfigurationJson
                    }),
                    "Updated",
                    _ => { Dialog.Close(true); return Task.CompletedTask; },
                    successTitleKey: "Updated");
            }
            else
            {
                await Ui.RunAsync(
                    () => Api.CreateServiceConnectionAsync(new CreateServiceConnectionRequest
                    {
                        Name = _model.Name,
                        Description = _model.Description,
                        Type = _model.Type,
                        ProjectId = _model.ProjectId,
                        Url = _model.Url,
                        ConfigurationJson = string.IsNullOrWhiteSpace(_model.ConfigurationJson) ? "{}" : _model.ConfigurationJson
                    }),
                    "Created",
                    _ => { Dialog.Close(true); return Task.CompletedTask; },
                    successTitleKey: "Created");
            }
        }
        finally { _busy = false; }
    }

    private void Cancel() => Dialog.Close(false);

    // Local form model so create/edit share one binding surface (Update has no Type; Create does).
    private sealed class EditModel
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public ServiceConnectionType Type { get; set; } = ServiceConnectionType.GitHub;

        public int? ProjectId { get; set; }

        [StringLength(500)]
        public string? Url { get; set; }

        [StringLength(8000)]
        public string ConfigurationJson { get; set; } = "{}";
    }
}
