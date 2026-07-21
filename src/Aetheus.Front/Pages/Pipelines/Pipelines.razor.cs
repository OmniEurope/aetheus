// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Pages.Pipelines;

// 0-a: the page now hosts two tabs - the Pipelines list (shared PipelinesList component) and the
// Templates manager (kept here). All pipeline list/filter/run/import logic lives in PipelinesList;
// this code-behind only drives the Templates tab + tab selection.
public partial class Pipelines : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;

    private int _selectedTab;

    // Templates
    private List<PipelineTemplateSummaryDto> _templates = [];
    private bool _templatesLoading;
    private bool _templatesLoaded;
    private bool _templatesLoadFailed;
    private InputFile? _fileInput;
    private bool _canTemplateWrite;

    protected override void OnInitialized()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanTemplateWrite();
        if (Nav.Uri.Contains("/templates", StringComparison.OrdinalIgnoreCase))
            _selectedTab = 1;

        Breadcrumb.Set(new BreadcrumbItem(L["Pipelines"]));
    }

    private void OnPermissionsChanged()
    {
        RefreshCanTemplateWrite();
        _ = InvokeAsync(StateHasChanged);
    }

    private void RefreshCanTemplateWrite() =>
        _canTemplateWrite = Permissions.CanWrite(Aetheus.Shared.Enums.ResourceType.PipelineTemplate);

    public ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        return ValueTask.CompletedTask;
    }

    private async Task OnTabChanged(int index)
    {
        var targetPath = index == 1 ? "templates" : "pipelines";
        var currentPath = Nav.ToBaseRelativePath(Nav.Uri).Split('?', 2)[0].TrimEnd('/');
        if (!string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase))
            Nav.NavigateTo($"/{targetPath}");

        if (index == 1 && !_templatesLoaded)
            await LoadTemplates();
    }

    private async Task LoadTemplates()
    {
        if (_templatesLoading) return;
        _templatesLoading = true;
        _templatesLoadFailed = false;
        try
        {
            _templates = await Api.GetPipelineTemplatesAsync();
            _templatesLoaded = true;
        }
        catch (HttpRequestException)
        {
            _templatesLoaded = false;
            _templatesLoadFailed = true;
        }
        finally
        {
            _templatesLoading = false;
        }
    }

    private async Task OnCreateTemplate()
    {
        var result = await Dialog.OpenAsync<TemplateEditDialog>(
            L["NewTemplate"],
            new Dictionary<string, object?>(),
            new DialogOptions { Width = "600px" });

        if (result is true)
        {
            Api.InvalidateTemplateCache();
            await LoadTemplates();
        }
    }

    private async Task OnEditTemplate(PipelineTemplateSummaryDto summary)
    {
        var template = await Api.GetPipelineTemplateAsync(summary.Id);
        if (template is null) return;

        var result = await Dialog.OpenAsync<TemplateEditDialog>(
            L["EditTemplate"],
            new Dictionary<string, object?> { { "Template", template } },
            new DialogOptions { Width = "600px" });

        if (result is true)
        {
            Api.InvalidateTemplateCache();
            await LoadTemplates();
        }
    }

    private async Task OnDeleteTemplate(PipelineTemplateSummaryDto template)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["DeleteConfirm"], template.Name),
            L["Delete"],
            new ConfirmOptions { OkButtonText = L["Delete"], CancelButtonText = L["Cancel"] });

        if (confirmed != true) return;

        var deleted = await Api.DeletePipelineTemplateAsync(template.Id);
        if (deleted)
        {
            Toast.Success("Deleted");
            await LoadTemplates();
        }
    }

    private async Task OnExportTemplate(PipelineTemplateSummaryDto template)
    {
        var full = await Api.GetPipelineTemplateAsync(template.Id);
        if (full is null) return;
        await Js.InvokeVoidAsync("downloadFile", $"{full.Name}.yaml", full.YamlContent, "application/x-yaml");
    }

    private async Task OnTemplateHistory(PipelineTemplateSummaryDto template)
    {
        await Dialog.OpenAsync<TemplateVersionHistoryDialog>(
            string.Format(L["VersionHistory"], template.Name),
            new Dictionary<string, object?>
            {
                ["TemplateId"] = template.Id,
                ["TemplateName"] = template.Name,
                ["LatestVersion"] = template.Version
            },
            new DialogOptions { Width = "80rem" });
    }

    private async Task OnImportTemplateClick()
    {
        if (_fileInput?.Element is not null)
            await Js.InvokeVoidAsync("HTMLElement.prototype.click.call", _fileInput.Element);
    }

    private async Task OnTemplateFileSelected(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file.Size == 0 || file.Size > 1_048_576) return;

        using var ms = new MemoryStream();
        await file.OpenReadStream(1_048_576).CopyToAsync(ms);
        var result = await Api.ImportPipelineTemplateAsync(ms.ToArray(), file.Name);
        if (result is not null)
        {
            Toast.Success("Imported");
            await LoadTemplates();
        }
    }
}
