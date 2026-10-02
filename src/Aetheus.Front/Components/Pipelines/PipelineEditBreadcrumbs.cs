// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// The breadcrumb of the pipeline editor. A pipeline owned by a project sits under that project's
/// pipelines (its name from the loaded pipeline, else from the project list, else its id); any other
/// pipeline sits under the global pipelines list.
/// </summary>
internal static class PipelineEditBreadcrumbs
{
    public static BreadcrumbItem[] Build(
        IStringLocalizer<AppStrings> l, bool isNew, PipelineDto? pipeline, int? projectId,
        IReadOnlyList<ProjectDto> projects)
    {
        var current = new BreadcrumbItem(isNew ? l["NewPipeline"] : pipeline?.Name ?? l["Pipeline"]);
        if (projectId is not { } ownerId)
            return [new BreadcrumbItem(l["Pipelines"], "/pipelines"), current];

        var projectName = pipeline?.ProjectName
            ?? projects.FirstOrDefault(project => project.Id == ownerId)?.Name
            ?? $"{l["Project"]} #{ownerId}";
        return
        [
            new BreadcrumbItem(l["Projects"], "/projects"),
            new BreadcrumbItem(projectName, $"/projects/{ownerId}/overview"),
            new BreadcrumbItem(l["Pipelines"], $"/projects/{ownerId}/pipelines"),
            current
        ];
    }
}
