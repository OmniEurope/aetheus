// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Services;

/// <summary>
/// One-shot handoff for "Import from YAML file" (S-FEAT-17): the pipelines list reads an uploaded
/// YAML and stashes it here, then routes to <c>/pipelines/new</c> where the edit form prefills the
/// definition and name so the user can pick an owner (ExactlyOneOwner) before saving. Scoped - the
/// value is consumed (and cleared) by the next new-pipeline page load.
/// </summary>
public sealed class PipelineImportState
{
    public string? PendingYaml { get; private set; }
    public string? PendingName { get; private set; }

    public void Set(string yaml, string? name)
    {
        PendingYaml = yaml;
        PendingName = name;
    }

    /// <summary>Returns the stashed import (if any) and clears it so it is applied only once.</summary>
    public (string Yaml, string? Name)? Consume()
    {
        if (string.IsNullOrEmpty(PendingYaml)) return null;
        var result = (PendingYaml, PendingName);
        PendingYaml = null;
        PendingName = null;
        return result;
    }
}
