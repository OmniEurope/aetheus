// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Git;

internal static class GitBlameParser
{
    public static List<GitLightBlameLine> Parse(string output)
    {
        var lines = new List<GitLightBlameLine>();
        var authors = new Dictionary<string, string>();
        var dates = new Dictionary<string, DateTime>();
        string? sha = null;
        var lineNumber = 0;
        foreach (var raw in output.Split('\n'))
        {
            if (TryAddContentLine(raw, sha, lineNumber, authors, dates, lines)) continue;
            if (TryReadHeader(raw, ref sha, ref lineNumber)) continue;
            ReadMetadata(raw, sha, authors, dates);
        }
        return lines;
    }

    private static bool TryAddContentLine(
        string raw,
        string? sha,
        int lineNumber,
        IReadOnlyDictionary<string, string> authors,
        IReadOnlyDictionary<string, DateTime> dates,
        ICollection<GitLightBlameLine> lines)
    {
        if (raw.Length == 0 || raw[0] != '\t') return raw.Length == 0;
        lines.Add(new GitLightBlameLine
        {
            LineNumber = lineNumber,
            Sha = sha ?? string.Empty,
            ShortSha = sha is { Length: >= 7 } ? sha[..7] : sha ?? string.Empty,
            AuthorName = sha is not null && authors.TryGetValue(sha, out var author) ? author : "unknown",
            AuthorDate = sha is not null && dates.TryGetValue(sha, out var date) ? date : default,
            Line = raw[1..]
        });
        return true;
    }

    private static bool TryReadHeader(string raw, ref string? sha, ref int lineNumber)
    {
        if (raw.Length < 40 || !IsHexString(raw.AsSpan(0, 40))) return false;
        var parts = raw.Split(' ');
        sha = parts[0];
        if (parts.Length >= 3 && int.TryParse(parts[2], out var parsedLineNumber))
            lineNumber = parsedLineNumber;
        return true;
    }

    private static void ReadMetadata(
        string raw,
        string? sha,
        IDictionary<string, string> authors,
        IDictionary<string, DateTime> dates)
    {
        if (raw.StartsWith("author ") && sha is not null)
            authors.TryAdd(sha, raw[7..]);
        else if (raw.StartsWith("author-time ") && sha is not null
                 && long.TryParse(raw[12..], out var timestamp))
            dates.TryAdd(sha, DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime);
    }

    private static bool IsHexString(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
            if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                return false;
        return true;
    }
}
