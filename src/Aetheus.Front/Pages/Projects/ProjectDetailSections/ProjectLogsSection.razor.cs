// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectLogsSection : ProjectPagedSectionBase<TaskLogDto>
{
    private PaginatedResult<TaskLogDto>? _result;
    protected override string DefaultSortBy => "Timestamp";
    protected override bool DefaultSortDescending => true;

    protected override async Task LoadAsync()
    {
        var projectId = ProjectId;
        _loading = true;
        PaginatedResult<TaskLogDto> result;
        try
        {
            result = await Api.Projects.GetProjectLogsAsync(
            projectId, _page, _pageSize, _search, _sortBy, _sortDescending);
        }
        catch (HttpRequestException) { result = new PaginatedResult<TaskLogDto>(); }
        if (ProjectId != projectId) return;
        _result = result;
        _loading = false;
    }

    private static BadgeStyle GetLevelBadge(TaskLogLevel level) => DisplayFormatting.TaskLogBadge(level);
}
