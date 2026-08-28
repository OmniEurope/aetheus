// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Pages.Pipelines;

namespace Aetheus.Front.Services;

/// <summary>
/// Shared pre-flight gate before launching a pipeline run. Resolves each stage's target server
/// up-front and, if any stage has no online agent, asks the user to confirm (or aborts). Invalid
/// YAML is surfaced here too so the user is never sent into a doomed run. Centralises the logic
/// previously duplicated in PipelineEdit and Pipelines.
/// </summary>
public sealed class PipelineRunGate(
    ApiClient api,
    DialogService dialog,
    NotifyHelper toast,
    IStringLocalizer<AppStrings> localizer)
{
    /// <summary>Returns <c>true</c> if the run may proceed, <c>false</c> to abort.</summary>
    public async Task<bool> ConfirmPreflightAsync(int pipelineId, string? sourceBranch = null)
    {
        var pre = await api.Pipelines.PreflightPipelineAsync(pipelineId, sourceBranch);

        if (pre.Error is not null)
        {
            toast.Notify(NotificationSeverity.Error, "PipelineRunFailed",
                string.Join(" ", pre.Error.Errors));
            return false;
        }

        // S-UX-TRGE: a 401/403/500 preflight previously left both Value and Error null and the gate
        // fell through to an empty stage list, returning true and dispatching a doomed run. Surface the
        // real reason and abort instead of silently proceeding.
        if (pre.IsTransportFailure)
        {
            var reasonKey = pre.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "PreflightSessionExpired",
                HttpStatusCode.Forbidden => "PreflightForbidden",
                _ => "PreflightServerError"
            };
            toast.Notify(NotificationSeverity.Error, "PipelineRunFailed", localizer[reasonKey].Value);
            return false;
        }

        var warnings = pre.Value?.Warnings ?? [];
        if (warnings.Count > 0)
        {
            toast.Notify(
                NotificationSeverity.Warning,
                "Warnings",
                string.Join("\n", warnings.Select(warning => $"• {warning}")));
        }

        var stages = pre.Value?.Stages ?? [];
        var unresolved = stages.Where(s => !s.Resolved).ToList();
        if (unresolved.Count == 0)
        {
            // Show which runners were picked (fallback transparency)
            var fallbacks = stages.Where(s => s.Resolved && s.ServerName is not null).ToList();
            if (fallbacks.Count > 0)
            {
                var info = string.Join("\n", fallbacks.Select(s =>
                    $"• {s.StageName}: {s.ServerName}" + (s.Reason is not null ? $" ({s.Reason})" : "")));
                toast.Notify(NotificationSeverity.Info, "PreflightResolved", info);
            }
            return true;
        }

        // A plain Confirm repeated one full sentence per unresolved stage, so an eleven-stage pipeline
        // rendered eleven copies of the same cause and the dialog became unreadable. The dedicated
        // dialog groups the stages by cause and offers the action that actually fixes it.
        var confirmed = await dialog.OpenAsync<PipelineRunPreflightDialog>(
            localizer["PreflightNoAgentTitle"].Value,
            new Dictionary<string, object?> { [nameof(PipelineRunPreflightDialog.Unresolved)] = unresolved },
            new DialogOptions
            {
                Width = "min(42rem, 92vw)",
                // claude-ui-patterns.md: the title must be announced before the first control.
                AutoFocusFirstElement = false
            });
        return confirmed is true;
    }
}
