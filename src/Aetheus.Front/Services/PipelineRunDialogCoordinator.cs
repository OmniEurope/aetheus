// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Resources;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Services;

/// <summary>Coordinates branch and queue-parameter dialogs before a pipeline launch.</summary>
public sealed class PipelineRunDialogCoordinator(
    ApiClient api,
    DialogService dialog,
    IStringLocalizer<AppStrings> localizer)
{
    public async Task<string?> ChooseBranchAsync(int pipelineId)
    {
        var source = await api.GetPipelineSourceAsync(pipelineId);
        if (source is not { RepositoryId: > 0 }) return null;

        var repositoryTask = api.GetGitRepoAsync(source.RepositoryId);
        var branchesTask = api.GetGitBranchesAsync(source.RepositoryId);
        await Task.WhenAll(repositoryTask, branchesTask).ConfigureAwait(false);

        var repository = await repositoryTask.ConfigureAwait(false);
        var branches = OrderBranches(await branchesTask.ConfigureAwait(false), repository?.DefaultBranch);
        if (branches.Count == 0) return null;

        var selectedBranch = branches.FirstOrDefault(branch => branch.IsDefault)?.Name
            ?? branches.FirstOrDefault(branch => string.Equals(
                branch.Name,
                repository?.DefaultBranch,
                StringComparison.Ordinal))?.Name
            ?? branches.FirstOrDefault(branch => string.Equals(
                branch.Name,
                source.Branch,
                StringComparison.Ordinal))?.Name
            ?? branches[0].Name;

        return await dialog.OpenAsync<PipelineBranchDialog>(
            localizer["ChooseBranch"].Value,
            new Dictionary<string, object?>
            {
                { "Branches", branches },
                { "SourceBranch", selectedBranch }
            },
            new DialogOptions { Width = "460px" }) as string;
    }

    internal static List<Aetheus.Shared.DTOs.GitLightBranchDto> OrderBranches(
        IEnumerable<Aetheus.Shared.DTOs.GitLightBranchDto> branches,
        string? defaultBranch) =>
        branches
            .OrderBy(branch =>
                branch.IsDefault || string.Equals(branch.Name, defaultBranch, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<(bool Proceed, Dictionary<string, string>? Parameters)> CollectParametersAsync(
        int pipelineId, string? sourceBranch = null)
    {
        List<Aetheus.Shared.DTOs.PipelineRunParameterDto> declared;
        try { declared = await api.GetPipelineRunParametersAsync(pipelineId, sourceBranch).ConfigureAwait(false); }
        catch (HttpRequestException) { return (false, null); }
        if (declared.Count == 0) return (true, null);

        var result = await dialog.OpenAsync<RunParametersDialog>(
            localizer["RunParameters"].Value,
            new Dictionary<string, object?> { { "Parameters", declared } },
            new DialogOptions { Width = "560px" });
        return result is Dictionary<string, string> values ? (true, values) : (false, null);
    }
}
