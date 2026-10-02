// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

internal static class PipelineRunLogSnapshot
{
    public static List<TaskLogDto> Merge(
        IEnumerable<TaskLogDto> snapshot,
        IEnumerable<TaskLogDto> streamedDuringSnapshot)
    {
        var merged = snapshot.ToList();
        foreach (var streamed in streamedDuringSnapshot)
        {
            var existingIndex = streamed.Id > 0
                ? merged.FindIndex(log => log.Id == streamed.Id)
                : -1;
            if (existingIndex >= 0)
                merged[existingIndex] = streamed;
            else
                merged.Add(streamed);
        }

        return merged
            .OrderBy(log => log.Timestamp)
            .TakeLast(5000)
            .ToList();
    }
}
