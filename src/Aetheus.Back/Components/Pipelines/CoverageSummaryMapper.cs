// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Pipelines;

internal static class CoverageSummaryMapper
{
    public static CoverageResult SelectCanonical(IEnumerable<CoverageResult> results) => results
        .OrderByDescending(result => result.LinesValid)
        .ThenByDescending(result => result.BranchesValid)
        .ThenByDescending(result => result.CreatedAt)
        .First();

    public static PipelineCoverageSummaryDto Map(CoverageResult result)
    {
        var files = string.IsNullOrEmpty(result.FilesJson)
            ? []
            : JsonSerializer.Deserialize<List<CoverageFileDto>>(result.FilesJson) ?? [];
        return new PipelineCoverageSummaryDto
        {
            LineRate = result.LineRate,
            BranchRate = result.BranchRate,
            LinesCovered = result.LinesCovered,
            LinesValid = result.LinesValid,
            BranchesCovered = result.BranchesCovered,
            BranchesValid = result.BranchesValid,
            RunId = result.PipelineRunId,
            Files = files
        };
    }

    public static List<CoverageAssemblyDto> MapAssemblies(CoverageResult result)
    {
        var files = string.IsNullOrEmpty(result.FilesJson)
            ? []
            : JsonSerializer.Deserialize<List<CoverageFileDto>>(result.FilesJson) ?? [];
        // Recette R-428: a file without a recorded package used to be dropped, so a report without
        // package names (or stored before the parser kept them) gave an empty table. It is grouped
        // under the project its path names instead, by the rule the page's coverage tree uses.
        return files
            .GroupBy(CoverageProjectName.Of, StringComparer.Ordinal)
            .Select(group =>
            {
                var covered = group.Sum(file => file.LinesCovered);
                var valid = group.Sum(file => file.LinesValid);
                return new CoverageAssemblyDto
                {
                    Name = group.Key,
                    LinesCovered = covered,
                    LinesValid = valid,
                    LineRate = valid == 0 ? 0 : (double)covered / valid
                };
            })
            .OrderBy(assembly => assembly.LineRate)
            .ThenBy(assembly => assembly.Name, StringComparer.Ordinal)
            .ToList();
    }
}
