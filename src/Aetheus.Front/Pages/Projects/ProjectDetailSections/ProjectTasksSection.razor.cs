// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectTasksSection : ProjectPagedSectionBase<ServerTaskDto>
{
    private PaginatedResult<ServerTaskDto>? _result;
    protected override string DefaultSortBy => "CreatedAt";
    protected override bool DefaultSortDescending => true;

    protected override async Task LoadAsync()
    {
        var projectId = ProjectId;
        _loading = true;
        PaginatedResult<ServerTaskDto> result;
        try
        {
            result = await Api.Projects.GetProjectTasksAsync(
            projectId, _page, _pageSize, _search, _sortBy, _sortDescending);
        }
        catch (HttpRequestException) { result = new PaginatedResult<ServerTaskDto>(); }
        if (ProjectId != projectId) return;
        _result = result;
        _loading = false;
    }

    private static BadgeStyle GetFailureBadge(string failureCode) =>
        failureCode == TaskFailureCodes.ToolError ? BadgeStyle.Warning : BadgeStyle.Danger;

    private static string GetFailureIcon(string failureCode) => failureCode switch
    {
        TaskFailureCodes.InfrastructureMismatch => "construction",
        TaskFailureCodes.TestsFailed => "science",
        TaskFailureCodes.BuildFailed => "build",
        _ => "report_problem"
    };

    private static string SourceServerName(ServerTaskDto task) =>
        string.IsNullOrWhiteSpace(task.ServerName) ? $"#{task.ServerId}" : task.ServerName;
}
