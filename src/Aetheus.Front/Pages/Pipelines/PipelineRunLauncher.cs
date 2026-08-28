// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

internal sealed class PipelineRunLauncher(
    ApiClient api,
    PipelineRunGate runGate,
    PipelineRunDialogCoordinator dialogs,
    NavigationManager navigation,
    NotifyHelper toast,
    IStringLocalizer<AppStrings> localizer)
{
    /// <summary>
    /// A plain launch: the declared parameters are taken at their defaults and no dialog opens, unless
    /// a required parameter has no default and the run genuinely cannot be built without an answer.
    /// </summary>
    public async Task<PipelineRunLaunchResult?> LaunchAsync(int pipelineId, string? sourceBranch)
    {
        if (!await runGate.ConfirmPreflightAsync(pipelineId, sourceBranch)) return null;
        var (proceed, parameters) = await dialogs.ResolveDefaultParametersAsync(pipelineId, sourceBranch);
        if (!proceed) return null;
        return await TriggerAsync(pipelineId, sourceBranch, parameters);
    }

    /// <summary>
    /// A launch the user already configured in the unified dialog: branch and parameters are taken as
    /// chosen, with no second prompt.
    /// </summary>
    public async Task<PipelineRunLaunchResult?> LaunchAsync(int pipelineId, PipelineLaunchChoice choice)
    {
        if (!await runGate.ConfirmPreflightAsync(pipelineId, choice.SourceBranch)) return null;
        return await TriggerAsync(pipelineId, choice.SourceBranch, choice.Parameters);
    }

    private async Task<PipelineRunLaunchResult?> TriggerAsync(
        int pipelineId, string? sourceBranch, Dictionary<string, string>? parameters)
    {
        var outcome = await api.Pipelines.TriggerPipelineRunAsync(pipelineId, parameters, sourceBranch);
        if (outcome.Value is not null)
        {
            navigation.NavigateTo(navigation.GetUriWithQueryParameter("tab", (string?)null));
            toast.Info("Running", "PipelineTriggered");
            PaginatedResult<PipelineRunDto>? runs = null;
            PipelineDto? pipeline = null;
            try
            {
                runs = await api.Pipelines.GetPipelineRunsPagedAsync(pipelineId, page: 1, pageSize: 25);
            }
            catch (HttpRequestException)
            {
            }
            try
            {
                pipeline = await api.Pipelines.GetPipelineAsync(pipelineId);
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
