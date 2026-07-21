// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class PipelineTemplateRunIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task CompletingPrepare_DispatchesResolvedTemplateSteps()
    {
        await using var factory = new AetheusWebApplicationFactory(fixture.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Set<Organization>().Select(item => item.Id).FirstAsync(cancellationToken: TestContext.Current.CancellationToken);
        var suffix = Guid.NewGuid().ToString("N");

        var server = new Server
        {
            Name = $"template-runner-{suffix}",
            Hostname = $"template-runner-{suffix}",
            Status = ServerStatus.Online,
            PipelineRunnerEnabled = true,
            LastHeartbeat = DateTime.UtcNow,
            OrganizationId = organizationId
        };
        var project = new Project
        {
            Name = $"template-project-{suffix}",
            OrganizationId = organizationId
        };
        var template = new PipelineTemplate
        {
            Name = $"template-{suffix}",
            Category = "CI",
            OrganizationId = organizationId,
            LatestVersion = 1
        };
        template.Versions.Add(new PipelineTemplateVersion
        {
            Version = 1,
            YamlContent = "name: template\nstages:\n  - name: Toolchain\n    steps:\n      - name: Inspect Git\n        shell: git --version",
            ChangelogEntry = "Initial version",
            CreatedByUsername = "integration"
        });
        db.AddRange(server, project, template);
        await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var pipeline = new Pipeline
        {
            Name = $"template-pipeline-{suffix}",
            ProjectId = project.Id,
            YamlDefinition = $"name: template-pipeline\nextends: {template.Name}@1\nstages: []"
        };
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var runService = scope.ServiceProvider.GetRequiredService<IPipelineRunService>();
        var run = await runService.TriggerRunAsync(pipeline.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(run);
        Assert.Contains("git --version", run.YamlSnapshot, StringComparison.Ordinal);

        var prepareTask = await db.Tasks.SingleAsync(item => item.PipelineRunId == run.Id, cancellationToken: TestContext.Current.CancellationToken);
        var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
        Assert.True(await taskService.StartTaskAsync(prepareTask.Id, ct: TestContext.Current.CancellationToken));
        Assert.True(await taskService.CompleteTaskAsync(prepareTask.Id,
            new TaskResultDto { Status = TaskExecutionStatus.Success, ExitCode = 0 }, ct: TestContext.Current.CancellationToken));

        var userTask = await db.Tasks.AsNoTracking()
            .SingleAsync(item => item.PipelineRunId == run.Id && item.Id != prepareTask.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Inspect Git", userTask.Name);
    }
}
