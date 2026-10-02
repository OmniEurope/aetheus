// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

public sealed class BackendModuleDependencyArchitectureTests
{
    [Fact]
    public void TasksModule_DoesNotDependOnPipelinesModule()
    {
        var root = FindRepoRoot();
        var tasksDirectory = Path.Combine(root, "src", "Aetheus.Back", "Components", "Tasks");
        var violations = RepositoryScan.Enumerate(tasksDirectory, "*.cs")
            .Where(path => File.ReadAllText(path).Contains(
                "Aetheus.Back.Components.Pipelines",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ProjectsModule_DoesNotDependOnAnalysisImplementation()
    {
        var root = FindRepoRoot();
        var projectsDirectory = Path.Combine(root, "src", "Aetheus.Back", "Components", "Projects");
        var violations = RepositoryScan.Enumerate(projectsDirectory, "*.cs")
            .Where(path =>
            {
                // Projects reads grades through the IProjectAnalysisGradeReader port only. The port lives in
                // Analysis since 2026-09-25 (layer guard: Analysis L7 must not reach up into Projects L8 to
                // implement it), so the namespace is allowed; its implementations are not.
                var source = File.ReadAllText(path);
                return source.Contains("AnalysisProjectSummaryRepository", StringComparison.Ordinal)
                    || source.Contains("ProjectAnalysisGradeRepository", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void PipelineRepository_ComposesLineageThroughRegisteredPort()
    {
        var root = FindRepoRoot();
        var facade = File.ReadAllText(Path.Combine(
            root, "src", "Aetheus.Back", "Components", "Pipelines", "PipelineRepository.cs"));
        var module = File.ReadAllText(Path.Combine(
            root, "src", "Aetheus.Back", "Components", "Pipelines", "PipelinesModuleExtensions.cs"));

        Assert.DoesNotContain("new PipelineRunLineageRepository", facade, StringComparison.Ordinal);
        Assert.Contains("IPipelineRunLineageReader runLineage", facade, StringComparison.Ordinal);
        Assert.Contains(
            "AddScoped<IPipelineRunLineageReader, PipelineRunLineageRepository>()",
            module,
            StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
