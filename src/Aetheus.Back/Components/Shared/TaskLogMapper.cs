// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Shared;

internal static class TaskLogMapper
{
    public static PaginatedResult<TaskLogDto> ToPaginatedResult(
        IEnumerable<TaskLog> items,
        int totalCount,
        int page,
        int pageSize) => new()
        {
            Items = items.Select(log => new TaskLogDto
            {
                Id = log.Id,
                TaskId = log.TaskId,
                Level = log.Level,
                Message = log.Message,
                Timestamp = log.Timestamp
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
}
