// SPDX-License-Identifier: EUPL-1.2
using System.Data.Common;
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Components.Projects;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aetheus.Back.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ProjectListInsightsIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ProjectListInsights_UsesAtMostFourBoundedPostgresCommands()
    {
        await fixture.ResetAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        int projectId;
        int parentRunId;
        int childRunId;
        await using (var seed = new AppDbContext(options))
        {
            var organization = new Organization
            {
                Name = $"org-{Guid.NewGuid():N}",
                Slug = $"s{Guid.NewGuid():N}"[..12]
            };
            seed.Organizations.Add(organization);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            var project = new Project
            {
                Name = "Measured projects list",
                OrganizationId = organization.Id
            };
            seed.Projects.Add(project);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            projectId = project.Id;
            seed.GitCommits.Add(new GitCommit
            {
                ProjectId = project.Id,
                Sha = new string('a', 40),
                Message = "Measured commit",
                CreatedAt = DateTime.UtcNow
            });
            var parentPipeline = new Pipeline
            {
                ProjectId = project.Id,
                Name = "parent",
                YamlDefinition = "name: parent\nstages: []"
            };
            var childPipeline = new Pipeline
            {
                ProjectId = project.Id,
                Name = "child",
                YamlDefinition = "name: child\nstages: []"
            };
            seed.Pipelines.AddRange(parentPipeline, childPipeline);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            var parentRun = new PipelineRun
            {
                PipelineId = parentPipeline.Id,
                Status = PipelineStatus.Running,
                StartedAt = DateTime.UtcNow.AddMinutes(-1)
            };
            var childRun = new PipelineRun
            {
                PipelineId = childPipeline.Id,
                Status = PipelineStatus.Running,
                StartedAt = DateTime.UtcNow
            };
            seed.PipelineRuns.AddRange(parentRun, childRun);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            parentRunId = parentRun.Id;
            childRunId = childRun.Id;
            seed.PipelineStepRuns.Add(new PipelineStepRun
            {
                PipelineRunId = parentRun.Id,
                StageName = "orchestrate",
                StepName = "child",
                TriggeredRunId = childRun.Id,
                Status = TaskExecutionStatus.Running
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var counter = new CommandCounter();
        var measuredOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(counter)
            .Options;
        await using var measured = new AppDbContext(measuredOptions);
        var repository = new ProjectRepository(
            measured,
            new ProjectAnalysisGradeRepository(measured));

        var insights = await repository.GetProjectListInsightsAsync(
            [projectId],
            DateTime.UtcNow.AddMinutes(-30),
            TestContext.Current.CancellationToken);

        var insight = Assert.Single(insights).Value;
        Assert.Equal(childRunId, insight.LastRunId);
        Assert.Equal(parentRunId, insight.ParentRunId);
        Assert.Equal("parent", insight.ParentRunName);
        Assert.InRange(counter.Count, 1, 4);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
