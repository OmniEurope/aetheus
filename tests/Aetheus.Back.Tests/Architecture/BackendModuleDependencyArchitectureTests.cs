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
                var source = File.ReadAllText(path);
                return source.Contains("Aetheus.Back.Components.Analysis", StringComparison.Ordinal)
                    || source.Contains("AnalysisProjectSummaryRepository", StringComparison.Ordinal);
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
