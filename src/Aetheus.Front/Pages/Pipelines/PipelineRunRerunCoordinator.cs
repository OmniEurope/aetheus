// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Pages.Pipelines;

internal sealed class PipelineRunRerunCoordinator(
    ApiClient api,
    NavigationManager navigation,
    NotifyHelper toast,
    DialogService dialog,
    IStringLocalizer<AppStrings> localizer)
{
    public static bool CanRerun(PipelineRunDto? run) => run is
    { Status: PipelineStatus.Success or PipelineStatus.Failed or PipelineStatus.Cancelled };

    public async Task RerunAsync(PipelineRunDto run, RadzenSplitButtonItem? item)
    {
        var mode = item?.Value switch
        {
            "sameCommit" => RerunMode.SnapshotSameCommit,
            "branchHead" => RerunMode.SnapshotBranchHead,
            _ => RerunMode.Current
        };

        if (mode == RerunMode.Current)
        {
            var parameterResult = await TryRerunWithParametersAsync(run);
            if (parameterResult == ParameterRerunResult.Handled) return;
            if (parameterResult == ParameterRerunResult.LoadFailed)
            {
                toast.Error("PipelineRunFailed", "LoadFailed");
                return;
            }
        }

        var rerun = await api.RerunPipelineRunAsync(run.Id, mode);
        if (rerun is not null)
            navigation.NavigateTo($"/pipelines/runs/{rerun.Id}", forceLoad: true);
        else
            toast.Error("PipelineRunFailed", "Error");
    }

    private async Task<ParameterRerunResult> TryRerunWithParametersAsync(PipelineRunDto run)
    {
        List<PipelineRunParameterDto> declared;
        try { declared = await api.GetPipelineRunParametersAsync(run.PipelineId); }
        catch (HttpRequestException) { return ParameterRerunResult.LoadFailed; }
        if (declared.Count == 0) return ParameterRerunResult.NoParameters;

        var result = await dialog.OpenAsync<RunParametersDialog>(
            localizer["RunParameters"].Value,
            new Dictionary<string, object?>
            {
                { "Parameters", declared },
                { "Prefill", run.Parameters }
            },
            new DialogOptions { Width = "560px" });

        if (result is not Dictionary<string, string> values) return ParameterRerunResult.Handled;

        var outcome = await api.TriggerPipelineRunAsync(run.PipelineId, values);
        if (outcome.Value is not null)
        {
            navigation.NavigateTo($"/pipelines/runs/{outcome.Value.Id}", forceLoad: true);
        }
        else
        {
            var reason = outcome.Error is not null ? string.Join(" ", outcome.Error.Errors)
                : outcome.NotFound ? localizer["RunNotFound"].Value
                : outcome.StatusCode == System.Net.HttpStatusCode.Forbidden ? localizer["PipelineRunForbidden"].Value
                : localizer["PipelineRunFailedNoReason"].Value;
            toast.Notify(NotificationSeverity.Error, "PipelineRunFailed", reason);
        }

        return ParameterRerunResult.Handled;
    }

    private enum ParameterRerunResult
    {
        NoParameters,
        Handled,
        LoadFailed
    }
}
