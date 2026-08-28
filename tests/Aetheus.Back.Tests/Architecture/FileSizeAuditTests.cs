// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Architectural guard: keeps the size of production C# files under control so that large
/// services and components are decomposed into real collaborator types before they grow unwieldy.
///
/// Threshold: <see cref="MaxLines"/> physical lines per file. The whitelist below documents
/// files that are deliberately allowed to exceed the threshold (typically generated artefacts
/// or central registration tables); add new entries with a justification comment.
///
/// When this test fails, prefer one of:
/// <list type="bullet">
///   <item>extracting a real collaborator class with its own name and responsibility
///         (e.g. <c>ServerHeartbeatService</c>), wired through DI - NOT a <c>partial</c> split,</item>
///   <item>moving cohesive helpers to a sibling type,</item>
///   <item>only as a last resort, adding the file to the whitelist with a comment.</item>
/// </list>
/// Do NOT split a class across <c>Foo.X.cs</c> <c>partial</c> files purely to dodge this budget:
/// <c>partial</c> is reserved for framework/generator needs (EF, Blazor code-behind, source
/// generators, the integration-test <c>Program</c> shim). See CLAUDE.md "Interdictions".
/// </summary>
public class FileSizeAuditTests
{
    private const int MaxLines = 600;

    /// <summary>
    /// Files that may legitimately exceed <see cref="MaxLines"/>. Justify every entry.
    /// Paths are repo-relative, forward slashes, case-insensitive.
    ///
    /// EMPTY, and meant to stay that way. The last entry was PipelineRunService, exempted in 2026-06
    /// on the grounds that its trigger → stage-advancement → task-creation cycle could not be split
    /// without "a callback redesign that risks the core CI/CD path". That redesign was done in
    /// 2026-08: the recursion is closed by passing IPipelineChildRunLauncher as a method parameter
    /// instead of injecting it, and the engine is now sixteen collaborators, all under budget.
    /// </summary>
    private static readonly HashSet<string> Whitelist = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void NoProductionFile_ExceedsLineBudget()
    {
        var repoRoot = FindRepoRoot();
        var projectDirs = new[]
        {
            Path.Combine(repoRoot, "src", "Aetheus.Back"),
            Path.Combine(repoRoot, "src", "Aetheus.Front"),
            Path.Combine(repoRoot, "src", "Aetheus.Shared"),
            Path.Combine(repoRoot, "src", "Aetheus.Agent.Core"),
            Path.Combine(repoRoot, "src", "Aetheus.Agent.Linux"),
            Path.Combine(repoRoot, "src", "Aetheus.Agent.Windows"),
            Path.Combine(repoRoot, "src", "Aetheus.Cli"),
        };

        var offenders = new List<(string RelPath, int Lines)>();

        // A360-75: .razor used to be outside this guard, which is how PipelineRun.razor reached 608
        // lines with nothing turning red. The budget applies to a component exactly as it does to a
        // class - a 600-line page is a god file whatever its extension - so the scan now covers both.
        // Closing it required extracting PipelineRunAiResultsCard from that page first; the whitelist
        // stays empty.
        foreach (var dir in projectDirs.Where(Directory.Exists))
        {
            foreach (var file in RepositoryScan.Enumerate(dir, "*.cs")
                         .Concat(EnumerateRazorFiles(dir)))
            {
                // Skip build outputs and tooling artefacts.
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;
                // EF Core migrations are auto-generated.
                if (file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;

                var lineCount = File.ReadAllLines(file).Length;
                if (lineCount <= MaxLines) continue;

                var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (Whitelist.Contains(rel)) continue;

                offenders.Add((rel, lineCount));
            }
        }

        Assert.True(offenders.Count == 0,
            $"The following production files exceed {MaxLines} lines. Extract real collaborators " +
            $"or whitelist with justification:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders.OrderByDescending(o => o.Lines).Select(o => $"  - {o.RelPath} ({o.Lines} lines)")));
    }

    /// <summary>
    /// Razor components, where present. Unlike the .cs scan this tolerates an empty result on purpose:
    /// only the front project has .razor files, so demanding a floor here would fail on every backend
    /// project for the wrong reason.
    /// </summary>
    private static IEnumerable<string> EnumerateRazorFiles(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.razor", SearchOption.AllDirectories)
            : [];

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
