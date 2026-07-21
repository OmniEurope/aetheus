// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Logging;

namespace Aetheus.Back.Components.Pipelines;

public static class CoverageResultParser
{
    // K: cap the per-file list so a huge report can't bloat the row / payload. Files are ordered
    // worst-coverage first so the cap keeps the entries most worth acting on.
    private const int MaxFiles = 5000;

    public static CoverageResult? Parse(string xmlContent, int runId, string? stageName, string? stepName, ILogger? logger = null)
    {
        try
        {
            // ReportGenerator emits the standard Cobertura SYSTEM doctype. Trusted agent uploads may
            // ignore that declaration, but SafeXml still disables external resolution and expansion.
            var doc = SafeXml.LoadTrustedReport(xmlContent);
            var root = doc.Root;
            if (root is null || !root.Name.LocalName.Equals("coverage", StringComparison.OrdinalIgnoreCase))
                return null;

            var files = ExtractFiles(root);

            return new CoverageResult
            {
                PipelineRunId = runId,
                StageName = stageName,
                StepName = stepName,
                LineRate = ParseDouble(root.Attribute("line-rate")?.Value),
                BranchRate = ParseDouble(root.Attribute("branch-rate")?.Value),
                LinesCovered = ParseInt(root.Attribute("lines-covered")?.Value),
                LinesValid = ParseInt(root.Attribute("lines-valid")?.Value),
                BranchesCovered = ParseInt(root.Attribute("branches-covered")?.Value),
                BranchesValid = ParseInt(root.Attribute("branches-valid")?.Value),
                FilesJson = files.Count == 0 ? null : JsonSerializer.Serialize(files)
            };
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException or ArgumentException)
        {
            logger?.LogWarning("CoverageResultParser: malformed XML (run {RunId}): {Message}", runId, ex.Message);
            return null;
        }
    }

    // Aggregates Cobertura <class> elements by filename into per-file line coverage. A file may map to
    // several classes (partial classes, nested types), so lines are summed across all classes sharing a
    // filename. Worst-covered files come first so the UI surfaces the gaps to fix.
    private static List<CoverageFileDto> ExtractFiles(XElement root)
    {
        var byLine = new Dictionary<(string Assembly, string File, int Line), int>();

        foreach (var cls in root.Descendants().Where(e => e.Name.LocalName == "class"))
        {
            var filename = cls.Attribute("filename")?.Value;
            if (string.IsNullOrWhiteSpace(filename)) continue;
            var assembly = cls.Ancestors()
                .FirstOrDefault(element => element.Name.LocalName == "package")?
                .Attribute("name")?.Value ?? string.Empty;

            var lines = cls.Descendants().Where(e => e.Name.LocalName == "line").ToList();
            if (lines.Count == 0) continue;

            foreach (var line in lines)
            {
                var lineNumber = ParseInt(line.Attribute("number")?.Value);
                if (lineNumber <= 0) continue;
                var key = (assembly, filename, lineNumber);
                byLine[key] = Math.Max(byLine.GetValueOrDefault(key), ParseInt(line.Attribute("hits")?.Value));
            }
        }

        return byLine
            .GroupBy(kv => (kv.Key.Assembly, kv.Key.File))
            .Select(group => new CoverageFileDto
            {
                Assembly = group.Key.Assembly,
                File = group.Key.File,
                LinesCovered = group.Count(line => line.Value > 0),
                LinesValid = group.Count(),
                LineRate = (double)group.Count(line => line.Value > 0) / group.Count()
            })
            .OrderBy(f => f.LineRate)
            .ThenByDescending(f => f.LinesValid)
            .Take(MaxFiles)
            .ToList();
    }

    private static double ParseDouble(string? value)
    {
        if (value is null) return 0;
        _ = double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result);
        return result;
    }

    private static int ParseInt(string? value)
    {
        if (value is null) return 0;
        _ = int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result);
        return result;
    }
}
