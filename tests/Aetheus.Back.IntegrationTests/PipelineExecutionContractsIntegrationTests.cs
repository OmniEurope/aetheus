// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Aetheus.Back.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class PipelineExecutionContractsIntegrationTests(PostgresFixture fixture)
    : RelationalTestBase(fixture)
{
    [Fact]
    public async Task ArtifactCollectionBarrier_RecognizesEveryActiveAndTerminalState()
    {
        await ResetAndMigrateAsync();
        var graph = await SeedGraphAsync();

        await using var db = NewContext();
        var repository = CreateRepository(db);
        foreach (var status in new[]
                 {
                     TaskExecutionStatus.Pending,
                     TaskExecutionStatus.Assigned,
                     TaskExecutionStatus.Running
                 })
        {
            var task = new ServerTask
            {
                ServerId = graph.ServerId,
                PipelineRunId = graph.RunId,
                Name = $"collect-{status}",
                Command = "artifacts",
                Operation = OperationKind.PipelineCollectArtifacts,
                Status = status,
                CreatedAt = DateTime.UtcNow
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.True(await repository.HasActiveArtifactCollectionAsync(
                graph.RunId, TestContext.Current.CancellationToken));
            db.Tasks.Remove(task);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        foreach (var status in new[]
                 {
                     TaskExecutionStatus.Success,
                     TaskExecutionStatus.Failed,
                     TaskExecutionStatus.Timeout,
                     TaskExecutionStatus.Cancelled
                 })
        {
            db.Tasks.Add(new ServerTask
            {
                ServerId = graph.ServerId,
                PipelineRunId = graph.RunId,
                Name = $"collect-{status}",
                Command = "artifacts",
                Operation = OperationKind.PipelineCollectArtifacts,
                Status = status,
                CreatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(await repository.HasActiveArtifactCollectionAsync(
            graph.RunId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TriggerCompletionCompareAndSwap_AllowsExactlyOneBackend()
    {
        await ResetAndMigrateAsync();
        var graph = await SeedGraphAsync();
        int stepId;
        await using (var seed = NewContext())
        {
            var step = new PipelineStepRun
            {
                PipelineRunId = graph.RunId,
                StageName = "orchestrate",
                StepName = "trigger-child",
                Status = TaskExecutionStatus.Running,
                TriggeredRunId = graph.RunId + 1000
            };
            seed.PipelineStepRuns.Add(step);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            stepId = step.Id;
        }

        await using var firstDb = NewContext();
        await using var secondDb = NewContext();
        var firstRepository = CreateRepository(firstDb);
        var secondRepository = CreateRepository(secondDb);
        var completedAt = DateTime.SpecifyKind(
            new DateTime(2026, 7, 30, 19, 30, 0), DateTimeKind.Utc);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            await gate.Task;
            return await firstRepository.TryResolveTriggeredStepAsync(
                stepId, TaskExecutionStatus.Success, 0, "{}", null, null, completedAt,
                TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);
        var second = Task.Run(async () =>
        {
            await gate.Task;
            return await secondRepository.TryResolveTriggeredStepAsync(
                stepId, TaskExecutionStatus.Success, 0, "{}", null, null, completedAt,
                TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);
        gate.SetResult();

        var results = await Task.WhenAll(first, second);
        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
    }

    [Fact]
    public async Task ToolchainExecutionFields_RoundTripWithLegacyNullsAndPublicFailureFields()
    {
        await ResetAndMigrateAsync();
        var graph = await SeedGraphAsync();

        await using (var seed = NewContext())
        {
            seed.Tasks.AddRange(
                new ServerTask
                {
                    ServerId = graph.ServerId,
                    Name = "legacy",
                    Command = "true",
                    CreatedAt = DateTime.UtcNow
                },
                new ServerTask
                {
                    ServerId = graph.ServerId,
                    Name = "toolchain",
                    Command = "dotnet test",
                    Executor = ExecutorType.Container,
                    ContainerToolchain = "dotnet-10",
                    ContainerShell = "sh",
                    FailureCode = "TestsFailed",
                    FailureReason = "Two tests failed.",
                    CreatedAt = DateTime.UtcNow
                });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verification = NewContext();
        var legacy = await verification.Tasks.AsNoTracking()
            .SingleAsync(task => task.Name == "legacy", TestContext.Current.CancellationToken);
        var toolchain = await verification.Tasks.AsNoTracking()
            .SingleAsync(task => task.Name == "toolchain", TestContext.Current.CancellationToken);

        Assert.Null(legacy.ContainerToolchain);
        Assert.Null(legacy.ContainerShell);
        Assert.Null(legacy.FailureCode);
        Assert.Null(legacy.FailureReason);
        Assert.Equal("dotnet-10", toolchain.ContainerToolchain);
        Assert.Equal("sh", toolchain.ContainerShell);
        Assert.Equal("TestsFailed", toolchain.FailureCode);
        Assert.Equal("Two tests failed.", toolchain.FailureReason);
    }

    [Fact]
    public async Task TriggeredRunLookupIndex_IsPresentAndUsedByPostgres()
    {
        await ResetAndMigrateAsync();
        var graph = await SeedGraphAsync();
        await using (var seed = NewContext())
        {
            seed.PipelineStepRuns.Add(new PipelineStepRun
            {
                PipelineRunId = graph.RunId,
                StageName = "orchestrate",
                StepName = "trigger-child",
                Status = TaskExecutionStatus.Running,
                TriggeredRunId = 424242
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var indexCommand = new NpgsqlCommand(
                         """
                         SELECT indexname
                         FROM pg_indexes
                         WHERE schemaname = 'public'
                           AND tablename = 'PipelineStepRuns'
                           AND indexname = 'IX_PipelineStepRuns_TriggeredRunId'
                         """,
                         connection))
        {
            Assert.Equal(
                "IX_PipelineStepRuns_TriggeredRunId",
                await indexCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }

        await using var explain = new NpgsqlCommand(
            """
            SET enable_seqscan = off;
            EXPLAIN (COSTS OFF)
            SELECT *
            FROM "PipelineStepRuns"
            WHERE "TriggeredRunId" = 424242
            """,
            connection);
        await using var reader = await explain.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var plan = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            plan.Add(reader.GetString(0));
        Assert.Contains(
            plan,
            line => line.Contains("IX_PipelineStepRuns_TriggeredRunId", StringComparison.Ordinal));
    }

    private PipelineRepository CreateRepository(Aetheus.Back.Data.AppDbContext db) =>
        new(
            db,
            TimeProvider.System,
            NullLogger<PipelineRepository>.Instance,
            new PipelineTaskLifecycleRepository(db, TimeProvider.System),
            new PipelineRunLineageRepository(db));

    private async Task<(int ServerId, int RunId)> SeedGraphAsync()
    {
        await using var db = NewContext();
        var organization = new Organization
        {
            Name = $"org-{Guid.NewGuid():N}",
            Slug = $"s{Guid.NewGuid():N}"[..12]
        };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var project = new Project
        {
            Name = $"project-{Guid.NewGuid():N}",
            OrganizationId = organization.Id
        };
        var server = new Server
        {
            Name = $"server-{Guid.NewGuid():N}",
            Hostname = "127.0.0.1",
            OrganizationId = organization.Id
        };
        db.Projects.Add(project);
        db.Servers.Add(server);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var pipeline = new Pipeline
        {
            Name = $"pipeline-{Guid.NewGuid():N}",
            ProjectId = project.Id,
            YamlDefinition = "name: pipeline\nstages: []"
        };
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var run = new PipelineRun
        {
            PipelineId = pipeline.Id,
            Status = PipelineStatus.Running,
            StartedAt = DateTime.UtcNow
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (server.Id, run.Id);
    }
}
