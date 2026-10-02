// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Mirrors the backend's git-strict storage path for project-owned pipelines
/// (<c>PipelineGitService</c>): their YAML lives at <c>.pipeline/&lt;slug&gt;.yaml</c> in the
/// project's internal repo. Used to surface the "git-managed" indicator (S-FEAT-19 / S-DES-24).
/// </summary>
public static class PipelineGitPath
{
    public static string ForPipeline(string name) => $".pipeline/{PipelineGitPathPolicy.Slugify(name)}.yaml";
}
