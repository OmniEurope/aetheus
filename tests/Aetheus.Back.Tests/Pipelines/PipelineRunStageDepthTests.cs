// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// The depth every step carries so a run can be read. Stages sharing a depth ran concurrently, and
/// the run view had no way to say so: it listed stages in definition order, and a stage further down
/// turned green while one above it was still going.
/// </summary>
public sealed class PipelineRunStageDepthTests
{
    private static PipelineRun Run(string? snapshot, params (string Stage, int Order)[] steps) => new()
    {
        Id = 1,
        YamlSnapshot = snapshot,
        StepRuns = [.. steps.Select((step, index) => new PipelineStepRun
        {
            Id = index + 1,
            StageName = step.Stage,
            StepName = "run",
            Order = step.Order
        })]
    };

    private static int? DepthOf(PipelineRunDto dto, string stage) =>
        dto.Steps.First(step => step.StageName == stage).StageDepth;

    [Fact]
    public void DepthFollowsTheDependencyGraphRatherThanTheDeclarationOrder()
    {
        const string snapshot = """
            name: p
            stages:
              - name: Build
                steps:
                  - name: run
                    shell: make
              - name: Publish
                depends_on: [Test]
                steps:
                  - name: run
                    shell: make
              - name: Test
                depends_on: [Build]
                steps:
                  - name: run
                    shell: make
            """;

        var dto = PipelineRunDtoMapper.MapRunToDto(Run(snapshot, ("Build", 0), ("Publish", 1), ("Test", 2)));

        Assert.Equal(0, DepthOf(dto, "Build"));
        Assert.Equal(1, DepthOf(dto, "Test"));
        Assert.Equal(2, DepthOf(dto, "Publish"));
    }

    [Fact]
    public void ConcurrentStagesShareADepth()
    {
        const string snapshot = """
            name: p
            stages:
              - name: Build
                steps:
                  - name: run
                    shell: make
              - name: PackA
                depends_on: [Build]
                steps:
                  - name: run
                    shell: make
              - name: PackB
                depends_on: [Build]
                steps:
                  - name: run
                    shell: make
            """;

        var dto = PipelineRunDtoMapper.MapRunToDto(Run(snapshot, ("Build", 0), ("PackA", 1), ("PackB", 2)));

        Assert.Equal(1, DepthOf(dto, "PackA"));
        Assert.Equal(1, DepthOf(dto, "PackB"));
    }

    [Fact]
    public void ARunWithoutASnapshotCarriesNoDepthRatherThanAPlausibleWrongOne()
    {
        var dto = PipelineRunDtoMapper.MapRunToDto(Run(null, ("Build", 0)));

        Assert.Null(DepthOf(dto, "Build"));
    }

    [Fact]
    public void AnUnparseableSnapshotCarriesNoDepth()
    {
        var dto = PipelineRunDtoMapper.MapRunToDto(Run("{{{ not yaml at all", ("Build", 0)));

        Assert.Null(DepthOf(dto, "Build"));
    }

    [Fact]
    public void ADependencyOnAStageThatDoesNotExistIsIgnoredRatherThanFatal()
    {
        const string snapshot = """
            name: p
            stages:
              - name: Build
                depends_on: [Ghost]
                steps:
                  - name: run
                    shell: make
            """;

        var dto = PipelineRunDtoMapper.MapRunToDto(Run(snapshot, ("Build", 0)));

        Assert.Equal(0, DepthOf(dto, "Build"));
    }

    [Fact]
    public void TheDepthComesFromTheSnapshotTheRunExecuted()
    {
        // A pipeline edited since the run must not change how that run reads.
        const string snapshot = """
            name: p
            stages:
              - name: Build
                steps:
                  - name: run
                    shell: make
              - name: Deploy
                depends_on: [Build]
                steps:
                  - name: run
                    shell: make
            """;
        var run = Run(snapshot, ("Build", 0), ("Deploy", 1));
        run.Pipeline = new Pipeline { Id = 2, YamlDefinition = "name: p\nstages: []" };

        var dto = PipelineRunDtoMapper.MapRunToDto(run);

        Assert.Equal(0, DepthOf(dto, "Build"));
        Assert.Equal(1, DepthOf(dto, "Deploy"));
    }
}
