// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Git;

internal static class GitCommitMessageReader
{
    public static async Task<Dictionary<string, string>> ReadAsync(
        GitProcessRunner git, string diskPath, IReadOnlyCollection<string> shas, CancellationToken ct)
    {
        if (shas.Count is < 1 or > 200 || shas.Any(sha => !GitUnifiedDiffParser.IsSha(sha)))
            throw new ArgumentException("Between 1 and 200 hexadecimal commit SHAs are required.", nameof(shas));

        var args = new List<string> { "log", "--no-walk", "--format=%H%n%s", "-z" };
        args.AddRange(shas.Select(sha => sha.ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase));
        var (exitCode, output, _) = await git.RunGitAsync(diskPath, args, ct).ConfigureAwait(false);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output)) return [];

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('\n');
            if (separator <= 0) continue;
            result[entry[..separator]] = entry[(separator + 1)..].TrimEnd('\r', '\n');
        }
        return result;
    }
}
