// SPDX-License-Identifier: EUPL-1.2
using System.Text;

namespace Aetheus.Back.Components.Git;

/// <summary>
/// Parses a unified <c>git diff --patch</c> into per-file <see cref="FileDiffDto"/> entries (path,
/// status, +/- counts, raw patch text). Extracted from <see cref="GitLightCliService"/> so the CLI
/// wrapper stays lean and the parse logic is unit-testable in isolation. Backs the commit-detail view.
/// </summary>
public static class GitUnifiedDiffParser
{
    private const string BlockMarker = "diff --git ";

    /// <summary>True when <paramref name="sha"/> is a plausible git object id: 4-64 hex chars.
    /// Defence-in-depth on a route-supplied sha before it reaches a git command.</summary>
    public static bool IsSha(string? sha) =>
        !string.IsNullOrEmpty(sha) && sha.Length is >= 4 and <= 64 && IsHex(sha);

    /// <summary>Splits a unified patch into file blocks and parses each into a <see cref="FileDiffDto"/>.</summary>
    public static PullRequestDiffDto Parse(
        string? patch,
        int maxFiles = int.MaxValue,
        bool sourceTruncated = false)
    {
        if (string.IsNullOrEmpty(patch))
            return new PullRequestDiffDto { IsTruncated = sourceTruncated };

        var files = new List<FileDiffDto>();
        int totalAdd = 0, totalDel = 0;
        var truncated = sourceTruncated;
        foreach (var block in SplitBlocks(patch))
        {
            if (files.Count >= Math.Max(1, maxFiles))
            {
                truncated = true;
                break;
            }
            var file = ParseBlock(block);
            if (file is null) continue;
            totalAdd += file.Additions;
            totalDel += file.Deletions;
            files.Add(file);
        }

        return new PullRequestDiffDto
        {
            FileDiffs = files,
            Stats = new DiffStatsDto { Additions = totalAdd, Deletions = totalDel, FilesChanged = files.Count },
            IsTruncated = truncated
        };
    }

    private static IEnumerable<string> SplitBlocks(string patch)
    {
        var sb = new StringBuilder();
        var started = false;
        foreach (var line in patch.Split('\n'))
        {
            if (line.StartsWith(BlockMarker, StringComparison.Ordinal))
            {
                if (started) { yield return sb.ToString(); sb.Clear(); }
                started = true;
            }
            if (started) sb.Append(line).Append('\n');
        }
        if (started && sb.Length > 0) yield return sb.ToString();
    }

    private static FileDiffDto? ParseBlock(string block)
    {
        var lines = block.Split('\n');
        if (lines.Length == 0) return null;

        var status = "modified";
        var binary = false;
        int add = 0, del = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("new file mode", StringComparison.Ordinal)) status = "added";
            else if (line.StartsWith("deleted file mode", StringComparison.Ordinal)) status = "deleted";
            else if (line.StartsWith("rename from", StringComparison.Ordinal)) status = "renamed";
            else if (line.StartsWith("Binary files", StringComparison.Ordinal)) binary = true;
            else if (line.Length > 0 && line[0] == '+' && !line.StartsWith("+++ ", StringComparison.Ordinal)) add++;
            else if (line.Length > 0 && line[0] == '-' && !line.StartsWith("--- ", StringComparison.Ordinal)) del++;
        }

        return new FileDiffDto
        {
            Path = ExtractPath(lines),
            Status = status,
            Additions = add,
            Deletions = del,
            Patch = binary ? null : block
        };
    }

    private static string ExtractPath(string[] lines)
    {
        // "+++ b/<path>" is the added/modified side; deletes point it at /dev/null so fall back to
        // "--- a/<path>"; last resort parse "diff --git a/x b/y".
        foreach (var line in lines)
            if (line.StartsWith("+++ b/", StringComparison.Ordinal))
                return line[6..].TrimEnd('\r');
        foreach (var line in lines)
            if (line.StartsWith("--- a/", StringComparison.Ordinal))
                return line[6..].TrimEnd('\r');

        var header = lines[0];
        var idx = header.IndexOf(" b/", StringComparison.Ordinal);
        if (idx > 0) return header[(idx + 3)..].TrimEnd('\r');
        return header.StartsWith(BlockMarker, StringComparison.Ordinal) ? header[BlockMarker.Length..].TrimEnd('\r') : "unknown";
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!((c >= '0' && c <= '9')
                  || (c >= 'a' && c <= 'f')
                  || (c >= 'A' && c <= 'F')))
                return false;
        return true;
    }
}
