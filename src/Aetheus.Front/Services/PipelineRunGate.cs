// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Resources;
using Microsoft.Extensions.Localization;
using Radzen;

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
        var pre = await api.PreflightPipelineAsync(pipelineId, sourceBranch);

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

        // Build a detailed message distinguishing blocking vs non-blocking
        var details = string.Join("\n", unresolved.Select(s =>
            $"• {s.StageName} → {s.Target}" + (s.Reason is not null ? $": {s.Reason}" : "")));
        var messageTemplate = localizer["PreflightNoAgentBody"].Value;
        var message = messageTemplate.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(messageTemplate, details)
            : $"{messageTemplate}\n{details}";
        var confirmed = await dialog.Confirm(
            message,
            localizer["PreflightNoAgentTitle"].Value,
            new ConfirmOptions
            {
                OkButtonText = localizer["RunAnyway"].Value,
                CancelButtonText = localizer["Cancel"].Value
            });
        return confirmed == true;
    }
}
