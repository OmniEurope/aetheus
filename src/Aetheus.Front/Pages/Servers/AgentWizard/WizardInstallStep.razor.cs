// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Aetheus.Front.Pages.Servers.AgentWizard;

public partial class WizardInstallStep
{
    [Parameter] public string Platform { get; set; } = "linux";
    [Parameter] public string ServerBaseUrl { get; set; } = string.Empty;
    [Parameter] public string SiteVersion { get; set; } = "dev";
    /// <summary>
    /// When set, exposes a "Copy install link" button that yields a one-shot URL pointing
    /// at <c>/api/agent/installer/{platform}?token=...</c>. The endpoint returns a ready-to-run
    /// bash / PowerShell installer pre-filled with this token (Admin only).
    /// </summary>
    [Parameter] public string? InstallerToken { get; set; }
    [Parameter] public bool IncludePipelineRunner { get; set; } = true;
    [Parameter] public bool IncludeServerManagement { get; set; }
    [Parameter] public bool IncludeDeploymentAgent { get; set; }
    [Parameter] public bool IncludePatchManagement { get; set; }
    [Parameter] public bool IncludeFirewallManagement { get; set; }
    [Parameter] public bool IncludeDocker { get; set; }
    [Parameter] public bool DevModeInsecureTls { get; set; }

    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private bool _linkCopied;

    private bool IsLinux => string.Equals(Platform, "linux", StringComparison.OrdinalIgnoreCase);
    private string DownloadUrl => IsLinux
        ? "/downloads/aetheus-agent-linux-x64.tar.gz"
        : "/downloads/aetheus-agent-win-x64.zip";
    private string DownloadLabel => (IsLinux ? L["WizardDownloadLinux"] : L["WizardDownloadWindows"]).Value;

    private string InstallerLink
    {
        get
        {
            var baseUrl = (ServerBaseUrl ?? string.Empty).TrimEnd('/');
            var version = string.IsNullOrWhiteSpace(SiteVersion) ? "dev" : SiteVersion;
            var token = Uri.EscapeDataString(InstallerToken ?? string.Empty);
            var platform = IsLinux ? "linux" : "windows";
            return $"{baseUrl}/api/agent/installer/{platform}?token={token}&version={Uri.EscapeDataString(version)}";
        }
    }

    private async Task OpenDownloadAsync()
    {
        await JS.InvokeVoidAsync("open", $"{ServerBaseUrl}{DownloadUrl}", "_blank");
    }

    private async Task CopyInstallerLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(InstallerToken)) return;
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", InstallerLink);
        _linkCopied = true;
    }
}
