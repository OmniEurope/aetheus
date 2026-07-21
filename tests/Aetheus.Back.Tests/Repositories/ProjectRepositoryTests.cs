// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Projects;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class ProjectRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ProjectRepository _repo;

    public ProjectRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new ProjectRepository(_db);
    }

    [Fact]
    public async Task GetProjectsPagedAsync_ReturnsPagedDefaultOrderByName()
    {
        _db.Projects.AddRange(
            new Project { Name = "Zeta", Description = "d" },
            new Project { Name = "Alpha", Description = "d" },
            new Project { Name = "Mid", Description = "d" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetProjectsPagedAsync(null, null, false, 1, 2, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, total);
        Assert.Equal(2, items.Count);
        Assert.Equal("Alpha", items[0].Name);
    }

    [Fact]
    public async Task GetProjectsPagedAsync_WithSearch_Filters()
    {
        _db.Projects.AddRange(
            new Project { Name = "Frontend", Description = "React app" },
            new Project { Name = "Backend", Description = "API" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetProjectsPagedAsync("Front", null, false, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetProjectsPagedAsync_WithStatus_Filters()
    {
        _db.Projects.AddRange(
            new Project { Name = "Active", Description = "d", Status = ProjectStatus.Active },
            new Project { Name = "Archived", Description = "d", Status = ProjectStatus.Archived }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetProjectsPagedAsync(null, null, false, 1, 10, ProjectStatus.Active, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetProjectsPagedAsync_WithAccessibleIds_Filters()
    {
        _db.Projects.AddRange(
            new Project { Name = "P1", Description = "d" },
            new Project { Name = "P2", Description = "d" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var ids = await _db.Projects.Select(p => p.Id).Take(1).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var (items, total) = await _repo.GetProjectsPagedAsync(null, null, false, 1, 10, accessibleIds: ids, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetProjectsPagedAsync_SortByNameDescending()
    {
        _db.Projects.AddRange(
            new Project { Name = "Alpha", Description = "d" },
            new Project { Name = "Zeta", Description = "d" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, _) = await _repo.GetProjectsPagedAsync(null, "name", true, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal("Zeta", items[0].Name);
    }

    [Fact]
    public async Task GetProjectsPagedAsync_SortByCreatedAt()
    {
        _db.Projects.AddRange(
            new Project { Name = "Old", Description = "d", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new Project { Name = "New", Description = "d", CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, _) = await _repo.GetProjectsPagedAsync(null, "createdat", false, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal("Old", items[0].Name);
    }

    [Fact]
    public async Task GetProjectsPagedAsync_SortByStatus()
    {
        _db.Projects.AddRange(
            new Project { Name = "P1", Description = "d", Status = ProjectStatus.Archived },
            new Project { Name = "P2", Description = "d", Status = ProjectStatus.Active }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, _) = await _repo.GetProjectsPagedAsync(null, "status", false, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetProjectDetailAsync_Found_IncludesPipelines()
    {
        var project = new Project { Name = "P", Description = "d" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.Pipelines.Add(new Pipeline { ProjectId = project.Id, Name = "Build", YamlDefinition = "name: Build" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetProjectDetailAsync(project.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Single(result.Pipelines);
    }

    [Fact]
    public async Task GetProjectDetailAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetProjectDetailAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveRunStepLabelsAsync_PrefersRunningStep_AndNormalizesStage()
    {
        var project = new Project { Name = "P", Description = "d" };
        var pipeline = new Pipeline { Name = "CI", Project = project };
        var run = new PipelineRun { Pipeline = pipeline, Status = PipelineStatus.Running, StartedAt = DateTime.UtcNow };
        run.StepRuns =
        [
            new PipelineStepRun { PipelineRun = run, StageName = "Build", StepName = "Pending", Status = TaskExecutionStatus.Pending, Order = 1 },
            new PipelineStepRun { PipelineRun = run, StageName = "System:QA", StepName = "Tests", Status = TaskExecutionStatus.Running, Order = 2 }
        ];
        _db.PipelineRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var labels = await _repo.GetActiveRunStepLabelsAsync(project.Id, ct: TestContext.Current.CancellationToken);

        Assert.Equal("QA · Tests", labels[run.Id]);
    }

    [Fact]
    public async Task FindProjectWithPipelinesAsync_Found()
    {
        var project = new Project { Name = "P", Description = "d" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindProjectWithPipelinesAsync(project.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindProjectAsync_Found()
    {
        var project = new Project { Name = "P", Description = "d" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindProjectAsync(project.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddProjectAsync_Persists()
    {
        await _repo.AddProjectAsync(new Project { Name = "New", Description = "d" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Projects.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveProjectAsync_Removes()
    {
        var project = new Project { Name = "Del", Description = "d" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveProjectAsync(project, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.Projects.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAllProjectsWithRepoUrlAsync_ReturnsOnlyWithRepoUrl()
    {
        _db.Projects.AddRange(
            new Project { Name = "WithUrl", Description = "d", RepositoryUrl = "https://github.com/org/repo" },
            new Project { Name = "NoUrl", Description = "d", RepositoryUrl = null },
            new Project { Name = "EmptyUrl", Description = "d", RepositoryUrl = "" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetAllProjectsWithRepoUrlAsync(ct: TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.Projects.Add(new Project { Name = "P", Description = "d" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.Projects.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    private async Task<(Project project, Pipeline pipeline, PipelineRun run, Server server)> SetupProjectWithPipelineRunAsync()
    {
        var project = new Project { Name = "P1", Description = "d" };
        _db.Projects.Add(project);
        var server = new Server { Name = "srv", Hostname = "h" };
        _db.Servers.Add(server);
        await _db.SaveChangesAsync();

        var pipeline = new Pipeline { Name = "pipe", ProjectId = project.Id, YamlDefinition = "" };
        _db.Pipelines.Add(pipeline);
        await _db.SaveChangesAsync();

        var run = new PipelineRun { PipelineId = pipeline.Id, Status = PipelineStatus.Running };
        _db.PipelineRuns.Add(run);
        await _db.SaveChangesAsync();

        _db.PipelineStepRuns.Add(new PipelineStepRun { PipelineRunId = run.Id, ServerId = server.Id, StepName = "s1", Status = TaskExecutionStatus.Pending });
        await _db.SaveChangesAsync();

        return (project, pipeline, run, server);
    }

    [Fact]
    public async Task GetServersForProjectAsync_ReturnsServers()
    {
        var (project, _, _, server) = await SetupProjectWithPipelineRunAsync();

        var result = await _repo.GetServersForProjectAsync(project.Id, ct: TestContext.Current.CancellationToken);
        Assert.Single(result);
        Assert.Equal(server.Name, result[0].Name);
    }

    [Fact]
    public async Task GetProjectServersPageAsync_ReturnsSecondPageAndTotal()
    {
        var project = new Project { Name = "Paged", Description = "d" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.ProjectServers.AddRange(Enumerable.Range(0, 205).Select(index => new ProjectServer
        {
            ProjectId = project.Id,
            Type = ProjectServerType.ExternalHost,
            DisplayName = $"Host-{index:D3}",
            Host = $"host-{index:D3}.example"
        }));
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, totalCount) = await _repo.GetProjectServersPageAsync(
            project.Id, null, "DisplayName", false, 2, 200, ct: TestContext.Current.CancellationToken);

        Assert.Equal(205, totalCount);
        Assert.Equal(5, items.Count);
        Assert.Equal("Host-200", items[0].DisplayName);
    }

    [Fact]
    public async Task GetTasksPagedAsync_ReturnsPaged()
    {
        var (project, _, run, server) = await SetupProjectWithPipelineRunAsync();

        for (var i = 0; i < 5; i++)
            _db.Tasks.Add(new ServerTask { ServerId = server.Id, Name = $"t{i}", Command = "echo", Executor = ExecutorType.Shell, Status = TaskExecutionStatus.Pending, PipelineRunId = run.Id });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetTasksPagedAsync(project.Id, null, null, false, 1, 3, ct: TestContext.Current.CancellationToken);
        Assert.Equal(5, total);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task GetTasksPagedAsync_ReturnsRequestedSecondPage()
    {
        var (project, _, run, server) = await SetupProjectWithPipelineRunAsync();
        for (var i = 0; i < 30; i++)
            _db.Tasks.Add(new ServerTask
            {
                ServerId = server.Id,
                Name = $"Task-{i:D2}",
                Command = "echo",
                Executor = ExecutorType.Shell,
                Status = TaskExecutionStatus.Pending,
                PipelineRunId = run.Id,
                CreatedAt = new DateTime(2026, 1, 1).AddMinutes(i)
            });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, totalCount) = await _repo.GetTasksPagedAsync(
            project.Id, null, "CreatedAt", false, 2, 25, ct: TestContext.Current.CancellationToken);

        Assert.Equal(30, totalCount);
        Assert.Equal(5, items.Count);
        Assert.Equal("Task-25", items[0].Name);
    }

    [Fact]
    public async Task GetLogsPagedAsync_ReturnsPaged()
    {
        var (project, _, run, server) = await SetupProjectWithPipelineRunAsync();

        var task = new ServerTask { ServerId = server.Id, Name = "t", Command = "echo", Executor = ExecutorType.Shell, Status = TaskExecutionStatus.Pending, PipelineRunId = run.Id };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        for (var i = 0; i < 5; i++)
            _db.TaskLogs.Add(new TaskLog { TaskId = task.Id, Message = $"log{i}", Timestamp = DateTime.UtcNow.AddMinutes(-i) });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetLogsPagedAsync(project.Id, null, null, false, 1, 3, ct: TestContext.Current.CancellationToken);
        Assert.Equal(5, total);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task GetLogsPagedAsync_ReturnsRequestedSecondPage()
    {
        var (project, _, run, server) = await SetupProjectWithPipelineRunAsync();
        var task = new ServerTask
        {
            ServerId = server.Id,
            Name = "logs",
            Command = "echo",
            Executor = ExecutorType.Shell,
            Status = TaskExecutionStatus.Pending,
            PipelineRunId = run.Id
        };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        for (var i = 0; i < 30; i++)
            _db.TaskLogs.Add(new TaskLog
            {
                TaskId = task.Id,
                Message = $"Log-{i:D2}",
                Timestamp = new DateTime(2026, 1, 1).AddMinutes(i)
            });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, totalCount) = await _repo.GetLogsPagedAsync(
            project.Id, null, "Timestamp", false, 2, 25, ct: TestContext.Current.CancellationToken);

        Assert.Equal(30, totalCount);
        Assert.Equal(5, items.Count);
        Assert.Equal("Log-25", items[0].Message);
    }

    [Fact]
    public async Task GetRecentTasksAsync_ReturnsLimited()
    {
        var (project, _, run, server) = await SetupProjectWithPipelineRunAsync();

        for (var i = 0; i < 5; i++)
            _db.Tasks.Add(new ServerTask { ServerId = server.Id, Name = $"t{i}", Command = "echo", Executor = ExecutorType.Shell, Status = TaskExecutionStatus.Pending, PipelineRunId = run.Id, CreatedAt = DateTime.UtcNow.AddMinutes(-i) });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetRecentTasksAsync(project.Id, 3, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetRecentPipelineRunsAsync_ReturnsLimited()
    {
        var (project, pipeline, _, _) = await SetupProjectWithPipelineRunAsync();

        for (var i = 0; i < 4; i++)
            _db.PipelineRuns.Add(new PipelineRun { PipelineId = pipeline.Id, Status = PipelineStatus.Success, StartedAt = DateTime.UtcNow.AddMinutes(-i) });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetRecentPipelineRunsAsync(project.Id, 3, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, result.Count);
    }

    public void Dispose() => _db.Dispose();
}
