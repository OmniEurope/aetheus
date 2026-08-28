// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components.Forms;

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineTemplates : IAsyncDisposable
{
    [Parameter] public bool Embedded { get; set; }

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;

    private List<PipelineTemplateSummaryDto> _templates = [];
    private InputFile? _fileInput;
    private bool _loading;
    private bool _loadFailed;
    private bool _canWrite;
    private HubConnection? _hubConnection;

    protected override async Task OnInitializedAsync()
    {
        if (!Embedded)
            Breadcrumb.Set(new BreadcrumbItem(L["PipelineTemplates"]));
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshPermission();
        await LoadTemplatesAsync();
        await StartRealtimeAsync();
    }

    private void OnPermissionsChanged()
    {
        RefreshPermission();
        _ = InvokeAsync(StateHasChanged);
    }

    private void RefreshPermission() => _canWrite = Permissions.CanWrite(ResourceType.PipelineTemplate);

    private async Task LoadTemplatesAsync()
    {
        if (_loading) return;
        _loading = true;
        _loadFailed = false;
        try { _templates = await Api.PipelineTemplates.GetPipelineTemplatesAsync(); }
        catch (HttpRequestException) { _templates = []; _loadFailed = true; }
        finally { _loading = false; }
    }

    private async Task StartRealtimeAsync()
    {
        try
        {
            _hubConnection = HubFactory.Create("entities");
            _hubConnection.On<ResourceType, int, string>("EntityChanged", (type, _, _) =>
                type == ResourceType.PipelineTemplate
                    ? InvokeAsync(ReloadFromRealtimeAsync)
                    : Task.CompletedTask);
            _hubConnection.RejoinOnReconnect(() => InvokeAsync(async () =>
            {
                await _hubConnection.InvokeAsync("JoinEntityUpdates", ResourceType.PipelineTemplate);
                await ReloadFromRealtimeAsync();
            }));
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinEntityUpdates", ResourceType.PipelineTemplate);
        }
        catch { /* SignalR is best-effort; local mutations still reload explicitly. */ }
    }

    private async Task ReloadFromRealtimeAsync()
    {
        await LoadTemplatesAsync();
        StateHasChanged();
    }

    private void CreateTemplate() => Nav.NavigateTo("/templates/new");
    private void EditTemplate(PipelineTemplateSummaryDto template) => Nav.NavigateTo($"/templates/{template.Id}");
    private void ViewHistory(PipelineTemplateSummaryDto template) => Nav.NavigateTo($"/templates/{template.Id}/versions");
    private void UseTemplate(PipelineTemplateSummaryDto template) => Nav.NavigateTo($"/pipelines/setup?templateId={template.Id}");

    private async Task DeleteTemplateAsync(PipelineTemplateSummaryDto template)
    {
        var confirmed = await Dialog.Confirm(string.Format(L["DeleteConfirm"], template.Name), L["Delete"],
            new ConfirmOptions { OkButtonText = L["Delete"], CancelButtonText = L["Cancel"] });
        if (confirmed != true) return;
        if (await Api.PipelineTemplates.DeletePipelineTemplateAsync(template.Id))
        {
            Toast.Success("Deleted");
            await LoadTemplatesAsync();
        }
    }

    private async Task ExportTemplateAsync(PipelineTemplateSummaryDto template)
    {
        var full = await Api.PipelineTemplates.GetPipelineTemplateAsync(template.Id);
        if (full is not null)
            await Js.InvokeVoidAsync("downloadFile", $"{full.Name}.yaml", full.YamlContent, "application/x-yaml");
    }

    private async Task OnImportClickAsync()
    {
        if (_fileInput?.Element is not null)
            await Js.InvokeVoidAsync("HTMLElement.prototype.click.call", _fileInput.Element);
    }

    private async Task OnFileSelectedAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file.Size == 0 || file.Size > 1_048_576) return;
        using var stream = new MemoryStream();
        await file.OpenReadStream(1_048_576).CopyToAsync(stream);
        var result = await Api.PipelineTemplates.ImportPipelineTemplateAsync(stream.ToArray(), file.Name);
        if (result is null) return;
        Toast.Success("Imported");
        await LoadTemplatesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        if (_hubConnection is null) return;
        try { await _hubConnection.InvokeAsync("LeaveEntityUpdates", ResourceType.PipelineTemplate); }
        catch { /* best-effort */ }
        await _hubConnection.DisposeAsync();
        _hubConnection = null;
    }
}
