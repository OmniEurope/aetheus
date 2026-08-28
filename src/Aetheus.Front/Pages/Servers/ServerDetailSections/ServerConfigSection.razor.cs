// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerConfigSection
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public int ServerId { get; set; }

    private string _configYaml = string.Empty;
    private bool _configExporting;
    private bool _configImportVisible;
    private string _configImportYaml = string.Empty;
    private bool _configValidating;
    private bool _configPreviewing;
    private bool _configDeploying;
    private ServerConfigValidationResult? _configValidation;
    private ServerConfigPreviewDto? _configPreview;

    private async Task ExportConfigAsync()
    {
        _configExporting = true;
        var yaml = await Api.Servers.ExportServerConfigAsync(ServerId);
        if (yaml is not null)
        {
            _configYaml = yaml;
            Toast.Success("Configuration", "ConfigExported");
        }
        else
        {
            Toast.Error("Configuration", "ConfigExportFailed");
        }
        _configExporting = false;
    }

    private async Task CopyConfigToClipboardAsync()
    {
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", _configYaml);
        Toast.Info("Configuration", "ConfigCopied");
    }

    private async Task ValidateConfigAsync()
    {
        if (string.IsNullOrWhiteSpace(_configImportYaml)) return;
        _configValidating = true;
        _configValidation = await Api.Servers.ValidateServerConfigAsync(ServerId, _configImportYaml);
        _configValidating = false;
    }

    private async Task PreviewConfigAsync()
    {
        if (string.IsNullOrWhiteSpace(_configImportYaml)) return;
        _configPreviewing = true;
        _configPreview = await Api.Servers.PreviewServerConfigAsync(ServerId, _configImportYaml);
        if (_configPreview is null)
            Toast.Error("Configuration", "ConfigPreviewFailed");
        _configPreviewing = false;
    }

    private async Task DeployConfigAsync()
    {
        if (string.IsNullOrWhiteSpace(_configImportYaml)) return;
        _configDeploying = true;
        var result = await Api.Servers.DeployServerConfigAsync(ServerId, _configImportYaml);
        if (result is not null)
        {
            Toast.Success("Configuration", "ConfigDeploySuccess", result.TasksCreated);
            _configImportVisible = false;
            _configPreview = null;
            _configImportYaml = string.Empty;
        }
        else
        {
            Toast.Error("Configuration", "ConfigDeployFailed");
        }
        _configDeploying = false;
    }

    private static BadgeStyle GetChangeBadgeStyle(string action) => action switch
    {
        "pull" or "create" or "deploy" or "enable" => BadgeStyle.Success,
        "update" => BadgeStyle.Info,
        "unchanged" => BadgeStyle.Light,
        _ => BadgeStyle.Light
    };
}
