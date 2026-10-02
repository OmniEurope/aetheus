// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.AiTasks;
namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// Loads and opens the AI results associated with a pipeline run.
/// </summary>
internal static class PipelineRunAiResults
{
    public static async Task<List<AiRunResultDto>> LoadAsync(
        ApiClient api,
        int pipelineRunId,
        CancellationToken ct)
    {
        try
        {
            return (await api.Ai.GetAiResultsAsync(
                pipelineRunId: pipelineRunId, ct: ct)).Items;
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public static Task OpenAsync(
        OmniDialogService dialog,
        IStringLocalizer<AppStrings> localizer,
        AiRunResultDto result) =>
        dialog.OpenAsync<AiResultDialog>(
            localizer["AiRunResult"],
            new Dictionary<string, object?> { ["InitialResult"] = result },
            new OmniDialogOptions { Width = "920px", Height = "80vh", AutoFocusFirstElement = false });
}
