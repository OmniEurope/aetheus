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
/// generators, and the integration-test <c>Program</c> shim).
/// </summary>
public class FileSizeAuditTests
{
    private const int MaxLines = 600;

    /// <summary>
    /// Files that may legitimately exceed <see cref="MaxLines"/>. Justify every entry.
    /// Paths are repo-relative, forward slashes, case-insensitive.
    /// </summary>
    private static readonly HashSet<string> Whitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        // TODO: large Razor section, can be split per-tab (containers / images / volumes / networks).
        "src/Aetheus.Front/Pages/Servers/ServerDetailSections/ServerDockerSection.razor.cs",
        // 9 Replace*Async methods carry IsRelational() guards - extract a heartbeat collaborator.
        "src/Aetheus.Back/Components/Servers/ServerRepository.cs",
        // Cohesive pipeline-execution engine: trigger → stage-advancement → task-creation are mutually
        // recursive (Execution↔Tasks call each other's private members), so a partial-free split into
        // <600-line collaborators would require a callback redesign that risks the core CI/CD path. The
        // stateless helpers ARE extracted (PipelineRunHelpers); the recursive engine is kept as one
        // non-partial class rather than a 4-file partial split (anti-partial pass 2026-06-25).
        "src/Aetheus.Back/Components/Pipelines/PipelineRunService.cs",
        // Single injected HTTP client fronting ~24 independent backend API domains. Consolidated from
        // 24 partial files into one non-partial class; a per-domain split would churn every front
        // injection/call site (`@inject ApiClient Api` → `Api.X`). Transport helpers + ApiResults
        // records are factored alongside (anti-partial pass 2026-06-25).
        "src/Aetheus.Front/Services/ApiClient.cs",
    };

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

        foreach (var dir in projectDirs.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(FileSizeAuditTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
