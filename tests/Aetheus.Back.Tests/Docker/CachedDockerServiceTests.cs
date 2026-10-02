// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Docker;
using Aetheus.Back.Data.Entities;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class CachedDockerServiceTests
{
    private readonly IDockerRepository _repo = Substitute.For<IDockerRepository>();
    private readonly DockerService _inner;
    private readonly CachedDockerService _sut;
    private readonly IMemoryCache _cache;

    public CachedDockerServiceTests()
    {
        _inner = new DockerService(_repo, Substitute.For<IAuditService>(), Substitute.For<Aetheus.Back.Components.Tasks.ITaskService>());
        _cache = new MemoryCache(new MemoryCacheOptions());
        _sut = new CachedDockerService(_inner, _cache);
    }

    [Fact]
    public async Task GetContainersAsync_CachesResult()
    {
        _repo.GetContainersAsync(1, TestContext.Current.CancellationToken).Returns(
        [
            new DockerContainer { ServerId = 1, ContainerId = "c1", Name = "web", Image = "nginx", State = "running", Status = "Up" }
        ]);

        var first = await _sut.GetContainersAsync(1, ct: TestContext.Current.CancellationToken);
        var second = await _sut.GetContainersAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.Single(first);
        Assert.Single(second);
        await _repo.Received(1).GetContainersAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetImagesAsync_CachesResult()
    {
        _repo.GetImagesAsync(1, TestContext.Current.CancellationToken).Returns(
        [
            new DockerImage { ServerId = 1, ImageId = "i1", Repository = "nginx", Tag = "latest", Size = "142MB" }
        ]);

        await _sut.GetImagesAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.GetImagesAsync(1, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).GetImagesAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetComposeStacksAsync_CachesResult()
    {
        _repo.GetComposeStacksAsync(1, TestContext.Current.CancellationToken).Returns([]);

        await _sut.GetComposeStacksAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.GetComposeStacksAsync(1, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).GetComposeStacksAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetNetworksAsync_CachesResult()
    {
        _repo.GetNetworksAsync(1, TestContext.Current.CancellationToken).Returns([]);

        await _sut.GetNetworksAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.GetNetworksAsync(1, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).GetNetworksAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetVolumesAsync_CachesResult()
    {
        _repo.GetVolumesAsync(1, TestContext.Current.CancellationToken).Returns([]);

        await _sut.GetVolumesAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.GetVolumesAsync(1, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).GetVolumesAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteActionAsync_InvalidatesCache()
    {
        _repo.GetContainersAsync(1, TestContext.Current.CancellationToken).Returns([]);
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.GetContainersAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.ExecuteActionAsync(1, new DockerActionRequest { ContainerId = "abc123def456", Action = DockerContainerAction.Stop }, ct: TestContext.Current.CancellationToken);
        await _sut.GetContainersAsync(1, ct: TestContext.Current.CancellationToken);

        await _repo.Received(2).GetContainersAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PullImageAsync_InvalidatesCache()
    {
        _repo.GetImagesAsync(1, TestContext.Current.CancellationToken).Returns([]);
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.GetImagesAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.PullImageAsync(1, new DockerPullImageRequest { Image = "nginx:latest" }, ct: TestContext.Current.CancellationToken);
        await _sut.GetImagesAsync(1, ct: TestContext.Current.CancellationToken);

        await _repo.Received(2).GetImagesAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RemoveImageAsync_InvalidatesCache()
    {
        _repo.GetImagesAsync(1, TestContext.Current.CancellationToken).Returns([]);
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.GetImagesAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.RemoveImageAsync(1, "sha256:abcdef1234560000000000000000000000000000000000000000000000000000", ct: TestContext.Current.CancellationToken);
        await _sut.GetImagesAsync(1, ct: TestContext.Current.CancellationToken);

        await _repo.Received(2).GetImagesAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteComposeActionAsync_InvalidatesCache()
    {
        _repo.GetComposeStacksAsync(1, TestContext.Current.CancellationToken).Returns([]);
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.GetComposeStacksAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.ExecuteComposeActionAsync(1, new DockerComposeActionRequest { StackName = "web", Action = DockerComposeAction.Up }, ct: TestContext.Current.CancellationToken);
        await _sut.GetComposeStacksAsync(1, ct: TestContext.Current.CancellationToken);

        await _repo.Received(2).GetComposeStacksAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PruneAsync_InvalidatesCache()
    {
        _repo.GetContainersAsync(1, TestContext.Current.CancellationToken).Returns([]);
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.GetContainersAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.PruneAsync(1, new DockerPruneRequest { Containers = true }, ct: TestContext.Current.CancellationToken);
        await _sut.GetContainersAsync(1, ct: TestContext.Current.CancellationToken);

        await _repo.Received(2).GetContainersAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UpdateResourceLimitsAsync_InvalidatesCache()
    {
        _repo.GetContainersAsync(1, TestContext.Current.CancellationToken).Returns([]);
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.GetContainersAsync(1, ct: TestContext.Current.CancellationToken);
        await _sut.UpdateResourceLimitsAsync(1, new DockerResourceLimitsRequest { ContainerId = "abc123def456", CpuLimit = 2 }, ct: TestContext.Current.CancellationToken);
        await _sut.GetContainersAsync(1, ct: TestContext.Current.CancellationToken);

        await _repo.Received(2).GetContainersAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetContainerLogsAsync_DoesNotCache()
    {
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.GetContainerLogsAsync(1, new DockerContainerLogsRequest { ContainerId = "abc123def456", Tail = 10 }, ct: TestContext.Current.CancellationToken);
        await _sut.GetContainerLogsAsync(1, new DockerContainerLogsRequest { ContainerId = "abc123def456", Tail = 10 }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(2).AddTaskAsync(Arg.Any<ServerTask>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InspectContainerAsync_PassThrough()
    {
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.InspectContainerAsync(1, "abc123def456", ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetComposeFileAsync_PassThrough()
    {
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.GetComposeFileAsync(1, "my-stack", ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SaveComposeFileAsync_PassThrough()
    {
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.SaveComposeFileAsync(1, new DockerComposeFileSaveRequest { StackName = "web", Content = "version: '3'" }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteShellCommandAsync_PassThrough()
    {
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.ExecuteShellCommandAsync(1, new DockerExecRequest { ContainerId = "abc123def456", Command = "ls" }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetContainerEnvVarsAsync_PassThrough()
    {
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.GetContainerEnvVarsAsync(1, "abc123def456", ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ListContainerFilesAsync_PassThrough()
    {
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.ListContainerFilesAsync(1, new DockerBrowseRequest { ContainerId = "abc123def456", Path = "/" }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BuildImageAsync_PassThrough()
    {
        _repo.ServerExistsAsync(1, TestContext.Current.CancellationToken).Returns(true);

        await _sut.BuildImageAsync(1, new DockerBuildRequest { ImageTag = "myapp:latest", DockerfileContent = "FROM nginx" }, ct: TestContext.Current.CancellationToken);

        await _repo.Received(1).AddTaskAsync(Arg.Any<ServerTask>(), TestContext.Current.CancellationToken);
    }
}
