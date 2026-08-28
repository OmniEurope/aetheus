// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineRunPreflightDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<PreflightStageDto> Unresolved { get; set; } = [];

    private List<CauseGroup> Groups => BuildGroups();

    /// <summary>One distinct blocking cause, the stages it blocks, and how to fix it.</summary>
    internal sealed record CauseGroup(string Reason, List<string> Stages, Remediation? Remediation);

    /// <summary>An action that resolves a cause, rather than a message telling the user to go find it.</summary>
    internal sealed record Remediation(string Explanation, string ActionLabel, string ActionIcon, string Href);

    private List<CauseGroup> BuildGroups() =>
        [.. Unresolved
            .GroupBy(stage => stage.Reason ?? L["PreflightUnknownReason"].Value, StringComparer.Ordinal)
            .Select(group => new CauseGroup(
                Shorten(group.Key),
                [.. group.Select(stage => stage.StageName)],
                RemediationFor(group.First())))];

    /// <summary>
    /// The backend repeats the stage name inside its own reason ("Stage 'Migrate': no online pipeline
    /// runner..."), which is redundant once the stages are listed as badges. Drop that prefix so equal
    /// causes actually group together instead of differing only by stage name.
    /// </summary>
    private static string Shorten(string reason)
    {
        var marker = "': ";
        var at = reason.IndexOf(marker, StringComparison.Ordinal);
        return at >= 0 && reason.StartsWith("Stage '", StringComparison.Ordinal)
            ? reason[(at + marker.Length)..]
            : reason;
    }

    /// <summary>
    /// Maps a cause to the page that fixes it. An unresolved environment target is nearly always a
    /// server that is not attached to that environment (deleting and re-adding an agent empties the
    /// binding), so the environment page is where the fix happens; an agent target points at the
    /// server itself.
    /// </summary>
    private Remediation? RemediationFor(PreflightStageDto stage) => stage.TargetKind switch
    {
        PreflightTargetKind.Environment => new Remediation(
            L["PreflightFixEnvironmentHint"].Value,
            L["PreflightFixEnvironmentAction"].Value,
            "layers",
            "environments"),
        PreflightTargetKind.Agent or PreflightTargetKind.Pool => new Remediation(
            L["PreflightFixServerHint"].Value,
            L["PreflightFixServerAction"].Value,
            "dns",
            "servers"),
        _ => null,
    };

    private Task GoAsync(string href)
    {
        Dialog.Close(false);
        Nav.NavigateTo(href);
        return Task.CompletedTask;
    }
}
