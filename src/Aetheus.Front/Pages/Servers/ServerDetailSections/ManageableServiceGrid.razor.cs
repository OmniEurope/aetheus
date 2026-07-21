// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

/// <summary>
/// Reusable manageable-services grid with row multi-select and a bulk-action bar. Drives both the
/// per-row actions (delegated to the host) and a single batched action over the checked rows
/// (<see cref="OnBulk"/>). Selection is tracked by service name so it survives the host re-creating
/// the <see cref="ServiceInfoDto"/> list on each heartbeat refresh.
/// </summary>
public partial class ManageableServiceGrid
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public IEnumerable<ServiceInfoDto> Services { get; set; } = [];

    /// <summary>true = installed grid (start/stop/restart/uninstall); false = available grid (install).</summary>
    [Parameter] public bool Installed { get; set; }

    /// <summary>Installed grid only: render the service name as an "open module" link instead of a logs link.</summary>
    [Parameter] public bool ShowModuleLink { get; set; }

    [Parameter] public bool CanManagePackages { get; set; }

    /// <summary>S-UX-OFFA: the target server is online. When false, every mutating action
    /// (start/stop/restart/install/uninstall) is disabled up-front - no agent is polling to run it, so a
    /// click would only queue a task that sits pending. Logs/open-module stay enabled (read-only).</summary>
    [Parameter] public bool ServerOnline { get; set; } = true;

    private string ActionTitle(string enabledTitle) => ServerOnline ? enabledTitle : L["AgentOfflineActionBlocked"];

    /// <summary>Whether the agent has the teamspeak-setup capability. TeamSpeak installs via its own
    /// root-owned helper (not apt), so its Install button gates on this rather than CanManagePackages.</summary>
    [Parameter] public bool CanInstallTeamspeak { get; set; }

    [Parameter] public string? BusyService { get; set; }

    [Parameter] public EventCallback<string> OnStart { get; set; }
    [Parameter] public EventCallback<string> OnStop { get; set; }
    [Parameter] public EventCallback<string> OnRestart { get; set; }
    [Parameter] public EventCallback<string> OnInstall { get; set; }
    [Parameter] public EventCallback<string> OnUninstall { get; set; }
    [Parameter] public EventCallback<string> OnLogs { get; set; }
    [Parameter] public EventCallback<string> OnOpenModule { get; set; }

    /// <summary>Fired with the action kind (Start/Stop/Restart/Install/Uninstall) and the checked names.</summary>
    [Parameter] public EventCallback<BulkServiceRequest> OnBulk { get; set; }

    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    private bool AllSelected => Services.Any() && Services.All(s => _selected.Contains(s.Name));

    private void ToggleAll(bool value)
    {
        _selected.Clear();
        if (value)
            foreach (var s in Services)
                _selected.Add(s.Name);
    }

    private void ToggleOne(string name, bool value)
    {
        if (value) _selected.Add(name);
        else _selected.Remove(name);
    }

    private void ClearSelection() => _selected.Clear();

    // Most managed services install via apt (the package-manage capability). A couple of dedicated
    // modules are NOT apt packages: teamspeak installs through its own root-owned setup helper (gated on
    // the teamspeak-setup capability). Anything that is neither apt-installable nor a module install
    // (e.g. certbot used to be) is not offered for install at all.
    internal static bool IsAptInstallable(string serviceName) =>
        Aetheus.Shared.Constants.ManageablePackages.IsManageableService(serviceName);

    // Services installed via a dedicated module helper instead of apt. TeamSpeak is the only one today.
    internal static bool IsModuleInstallable(string serviceName) =>
        serviceName.Equals("teamspeak", StringComparison.OrdinalIgnoreCase);

    // Whether the Install action can be offered for this service given the server's capabilities.
    private bool CanInstall(string serviceName) =>
        IsModuleInstallable(serviceName) ? CanInstallTeamspeak
        : CanManagePackages && IsAptInstallable(serviceName);

    private string InstallTitle(string serviceName)
    {
        if (IsModuleInstallable(serviceName))
            return CanInstallTeamspeak ? L["Install"] : L["TeamspeakSetupCapabilityDisabled"];
        if (!CanManagePackages)
            return L["ServiceInstallCapabilityDisabled"];
        return IsAptInstallable(serviceName) ? L["Install"] : L["ServiceNotAptInstallable"];
    }

    private static void OnRowRenderInternal(RowRenderEventArgs<ServiceInfoDto> args)
    {
        if (args.Data is { IsInstalled: false })
            args.Attributes["class"] = "rz-color-secondary";
    }

    private async Task FireBulkAsync(string kind)
    {
        // Snapshot in the current data order so the batch matches what the user sees. For Install, drop
        // any checked service that can't actually be installed on this server (capability missing, or not
        // installable at all) so the batch can't report a confusing partial count for impossible actions.
        var names = Services
            .Where(s => _selected.Contains(s.Name))
            .Where(s => kind != "Install" || CanInstall(s.Name))
            .Select(s => s.Name)
            .ToList();
        if (names.Count == 0) return;
        await OnBulk.InvokeAsync(new BulkServiceRequest(kind, names));
        _selected.Clear();
    }
}

/// <summary>A batched service action requested from the grid's bulk bar.</summary>
public sealed record BulkServiceRequest(string Kind, IReadOnlyList<string> Services);
