// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

internal static class PipelineActionBreadcrumbs
{
    public static void Set(
        BreadcrumbService breadcrumb,
        IStringLocalizer<AppStrings> localizer,
        int pipelineId,
        PipelineDto? pipeline,
        string action)
    {
        var pipelineItem = new BreadcrumbItem(
            pipeline?.Name ?? $"{localizer["Pipeline"]} #{pipelineId}",
            $"/pipelines/{pipelineId}");
        if (pipeline?.ProjectId is { } projectId)
        {
            breadcrumb.Set(
                new BreadcrumbItem(localizer["Projects"], "/projects"),
                new BreadcrumbItem(
                    pipeline.ProjectName ?? $"{localizer["Project"]} #{projectId}",
                    $"/projects/{projectId}/overview"),
                new BreadcrumbItem(localizer["Pipelines"], $"/projects/{projectId}/pipelines"),
                pipelineItem,
                new BreadcrumbItem(action));
            return;
        }

        breadcrumb.Set(
            new BreadcrumbItem(localizer["Pipelines"], "/pipelines"),
            pipelineItem,
            new BreadcrumbItem(action));
    }
}
