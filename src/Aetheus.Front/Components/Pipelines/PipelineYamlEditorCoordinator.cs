// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// Performs API-backed validation and autocomplete updates for the pipeline YAML editor.
/// </summary>
internal sealed class PipelineYamlEditorCoordinator(
    ApiClient api,
    IStringLocalizer<AppStrings> localizer)
{
    public async Task<bool> ValidateAsync(string yaml, MonacoEditor? editor)
    {
        var valid = await api.Packages.ValidatePipelineYamlAsync(yaml) is not null;
        if (editor is null) return valid;
        if (valid)
        {
            await editor.ClearValidationErrorsAsync();
        }
        else
        {
            await editor.SetValidationErrorsAsync(
            [
                new MonacoValidationError { Message = localizer["InvalidYamlDefinition"].Value }
            ]);
        }
        return valid;
    }

    public async Task<(List<string> ServerNames, List<string> LibraryNames, List<string> VaultNames)>
        LoadSuggestionsAsync(int? projectId, MonacoEditor? editor)
    {
        var libraryNamesTask = api.Variables.GetVariableLibraryNamesAsync(projectId);
        var vaultNamesTask = api.Variables.GetVaultNamesAsync(projectId);
        var serverNamesTask = api.Servers.GetServerNamesAsync();
        var variableKeysTask = LoadVariableKeysAsync(projectId);
        await Task.WhenAll(libraryNamesTask, vaultNamesTask, serverNamesTask, variableKeysTask);

        var libraryNames = await libraryNamesTask;
        var vaultNames = await vaultNamesTask;
        var serverNames = await serverNamesTask;
        var variableKeys = await variableKeysTask;
        if (editor is not null) await editor.UpdateSuggestionsAsync(new MonacoSuggestions
        {
            LibraryNames = libraryNames,
            VaultNames = vaultNames,
            ServerNames = serverNames,
            VariableKeys = variableKeys
        });
        return (serverNames, libraryNames, vaultNames);
    }

    private async Task<List<string>> LoadVariableKeysAsync(int? projectId)
    {
        const int pageSize = PaginationRequest.MaxPageSize;
        var keys = new List<string>();
        for (var page = 1; ; page++)
        {
            var result = await api.Variables.GetVariableSuggestionKeysAsync(page, pageSize, projectId);
            keys.AddRange(result.Items);
            if (keys.Count >= result.TotalCount || result.Items.Count == 0) return keys;
        }
    }
}
