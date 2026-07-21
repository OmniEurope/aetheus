// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// Proves the paged-runs LIST projection (<see cref="PipelineRunHelpers.RunListProjection"/>, used by
/// <c>GetRunsPagedAsync</c>) actually TRANSLATES to SQL on real PostgreSQL and returns correct data.
/// The unit suite only mocks the repository, so a projection that fails to translate (the nested
/// <c>StepRuns.OrderBy().Select().ToList()</c> collection projection over a Skip/Take page, or the
/// navigation ternaries) would 500 the runs endpoint in production while every unit test stays green.
/// This is the InMemory-blind layer: EF InMemory client-evaluates anything, so it never surfaces a
/// non-translatable Expression - only a real Npgsql query does.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PipelineRunsProjectionIntegrationTests(PostgresFixture fixture)
    : RelationalTestBase(fixture)
{
    [Fact]
    public async Task GetRunsPagedAsync_TranslatesToSql_AndProjectsRunPlusStepSummary()
    {
        await ResetAndMigrateAsync();

        int pipelineId;
        int newerRunId;
        await using (var db = NewContext())
        {
            var org = new Organization { Name = $"org-{Guid.NewGuid():N}", Slug = $"s{Guid.NewGuid():N}"[..12], Description = "proj-test" };
            db.Set<Organization>().Add(org);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            var project = new Project { Name = $"proj-{Guid.NewGuid():N}", RepositoryUrl = "https://git/x.git", OrganizationId = org.Id };
            db.Projects.Add(project);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            var pipeline = new Pipeline { Name = $"pl-{Guid.NewGuid():N}", YamlDefinition = "name: x", ProjectId = project.Id };
            db.Pipelines.Add(pipeline);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            pipelineId = pipeline.Id;

            var older = new PipelineRun { PipelineId = pipeline.Id, Status = PipelineStatus.Success, StartedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            var newer = new PipelineRun { PipelineId = pipeline.Id, Status = PipelineStatus.Running, StartedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), BranchName = "main", CommitHash = "abc123" };
            db.PipelineRuns.AddRange(older, newer);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            newerRunId = newer.Id;

            // Steps deliberately inserted out of Order to prove the nested OrderBy(s => s.Order) translates.
            db.PipelineStepRuns.AddRange(
                new PipelineStepRun { PipelineRunId = newer.Id, StageName = "build", StepName = "second", Order = 2, Status = TaskExecutionStatus.Pending },
                new PipelineStepRun { PipelineRunId = newer.Id, StageName = "build", StepName = "first", Order = 1, Status = TaskExecutionStatus.Success });
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var db = NewContext())
        {
            var repo = new PipelineRepository(db, TimeProvider.System, NullLogger<PipelineRepository>.Instance);

            // If RunListProjection is not SQL-translatable, this throws (InvalidOperationException) here.
            var (items, total) = await repo.GetRunsPagedAsync(pipelineId, page: 1, pageSize: 10, ct: TestContext.Current.CancellationToken);

            Assert.Equal(2, total);
            // Ordered by StartedAt DESC: the newer run leads.
            Assert.Equal(newerRunId, items[0].Id);
            Assert.Equal(PipelineStatus.Running, items[0].Status);
            Assert.Equal("main", items[0].BranchName);
            Assert.Equal("abc123", items[0].CommitHash);

            // Step summary projected + ordered by Order (heavy fields intentionally absent from the list DTO).
            Assert.Equal(2, items[0].Steps.Count);
            Assert.Equal("first", items[0].Steps[0].StepName);
            Assert.Equal("second", items[0].Steps[1].StepName);
        }
    }
}
