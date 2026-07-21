// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Pipelines;

internal sealed class PipelineRunLauncher(
    ApiClient api,
    PipelineRunGate runGate,
    PipelineRunDialogCoordinator dialogs,
    NavigationManager navigation,
    NotifyHelper toast,
    IStringLocalizer<AppStrings> localizer)
{
    public async Task<PipelineRunLaunchResult?> LaunchAsync(int pipelineId, string? sourceBranch)
    {
        if (!await runGate.ConfirmPreflightAsync(pipelineId, sourceBranch)) return null;
        var (proceed, parameters) = await dialogs.CollectParametersAsync(pipelineId, sourceBranch);
        if (!proceed) return null;

        var outcome = await api.TriggerPipelineRunAsync(pipelineId, parameters, sourceBranch);
        if (outcome.Value is not null)
        {
            navigation.NavigateTo(navigation.GetUriWithQueryParameter("tab", (string?)null));
            toast.Info("Running", "PipelineTriggered");
            PaginatedResult<PipelineRunDto>? runs = null;
            PipelineDto? pipeline = null;
            try
            {
                runs = await api.GetPipelineRunsPagedAsync(pipelineId, page: 1, pageSize: 25);
            }
            catch (HttpRequestException)
            {
            }
            try
            {
                pipeline = await api.GetPipelineAsync(pipelineId);
            }
            catch (HttpRequestException)
            {
            }
            return new PipelineRunLaunchResult(outcome.Value, runs, pipeline);
        }

        var reason = outcome.Error is not null ? string.Join(" ", outcome.Error.Errors)
            : outcome.NotFound ? localizer["RunNotFound"].Value
            : outcome.StatusCode == System.Net.HttpStatusCode.Forbidden ? localizer["PipelineRunForbidden"].Value
            : localizer["PipelineRunFailedNoReason"].Value;
        toast.Notify(NotificationSeverity.Error, "PipelineRunFailed", reason);
        return null;
    }
}
