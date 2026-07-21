// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Back.Tests.Repositories;

/// <summary>
/// Focused coverage for <see cref="PipelineRepository.GetCompletedStageNamesAsync"/>, especially the
/// ContinueOnError nuance: a stage whose only failure is a step marked ContinueOnError still counts
/// as completed, whereas a hard failure does not.
/// </summary>
public class GetCompletedStageNamesTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PipelineRepository _repo;

    public GetCompletedStageNamesTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new PipelineRepository(_db, TimeProvider.System, NullLogger<PipelineRepository>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedStepAsync(int runId, string stage, TaskExecutionStatus status, bool continueOnError = false)
    {
        _db.PipelineStepRuns.Add(new PipelineStepRun
        {
            PipelineRunId = runId,
            StageName = stage,
            StepName = $"{stage}-{Guid.NewGuid():N}",
            Status = status,
            ContinueOnError = continueOnError,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task ContinueOnErrorFailure_CountsStageAsCompleted()
    {
        await SeedStepAsync(1, "Build", TaskExecutionStatus.Success);
        await SeedStepAsync(1, "Build", TaskExecutionStatus.Failed, continueOnError: true);

        var completed = await _repo.GetCompletedStageNamesAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Contains("Build", completed);
    }

    [Fact]
    public async Task HardFailure_DoesNotCountStageAsCompleted()
    {
        await SeedStepAsync(1, "Build", TaskExecutionStatus.Success);
        await SeedStepAsync(1, "Build", TaskExecutionStatus.Failed, continueOnError: false);

        var completed = await _repo.GetCompletedStageNamesAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Build", completed);
    }

    [Fact]
    public async Task RunningStep_DoesNotCountStageAsCompleted()
    {
        await SeedStepAsync(1, "Build", TaskExecutionStatus.Success);
        await SeedStepAsync(1, "Build", TaskExecutionStatus.Running);

        var completed = await _repo.GetCompletedStageNamesAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Build", completed);
    }

    [Fact]
    public async Task AllSuccessOrCancelled_CountsStageAsCompleted()
    {
        await SeedStepAsync(2, "Test", TaskExecutionStatus.Success);
        await SeedStepAsync(2, "Test", TaskExecutionStatus.Cancelled);

        var completed = await _repo.GetCompletedStageNamesAsync(2, ct: TestContext.Current.CancellationToken);

        Assert.Contains("Test", completed);
    }

    [Fact]
    public async Task HardFailure_CountsStageAsTerminal()
    {
        await SeedStepAsync(3, "E2E", TaskExecutionStatus.Success);
        await SeedStepAsync(3, "E2E", TaskExecutionStatus.Failed);

        var terminal = await _repo.GetTerminalStageNamesAsync(3, ct: TestContext.Current.CancellationToken);

        Assert.Contains("E2E", terminal);
    }

    [Fact]
    public async Task RunningStep_DoesNotCountStageAsTerminal()
    {
        await SeedStepAsync(4, "E2E", TaskExecutionStatus.Failed);
        await SeedStepAsync(4, "E2E", TaskExecutionStatus.Running);

        var terminal = await _repo.GetTerminalStageNamesAsync(4, ct: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("E2E", terminal);
    }
}
