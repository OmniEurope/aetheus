// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.Docker;

public class DockerService(IDockerRepository repo, IAuditService audit, ITaskService taskService) : IDockerService
{
    // Persist a queued task AND push the "TaskQueued" SignalR event so the top-bar tracker shows it
    // live (and can later flip it Running/Completed). Mirrors ServerServiceManager (see ITaskService).
    private async Task QueueTaskAsync(ServerTask task, CancellationToken ct = default)
    {
        await repo.AddTaskAsync(task, ct).ConfigureAwait(false);
        await taskService.NotifyTaskQueuedAsync(task, ct: ct).ConfigureAwait(false);
    }

    public async Task<List<DockerContainerDto>> GetContainersAsync(int serverId, CancellationToken ct = default)
    {
        var containers = await repo.GetContainersAsync(serverId, ct).ConfigureAwait(false);
        return containers.Select(c => new DockerContainerDto
        {
            ContainerId = c.ContainerId,
            Name = c.Name,
            Image = c.Image,
            State = c.State,
            Status = c.Status,
            Ports = c.Ports,
            Created = c.Created,
            CpuPercent = c.CpuPercent,
            MemoryUsageMb = c.MemoryUsageMb,
            MemoryLimitMb = c.MemoryLimitMb
        }).ToList();
    }

    public async Task ExecuteActionAsync(int serverId, DockerActionRequest request, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidContainerId(request.ContainerId))
            throw new BadRequestException("Invalid container ID.");

        var dockerCommand = MapActionToCommand(request.Action);
        var command = $"docker {dockerCommand} {request.ContainerId}";

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker {request.Action} - {request.ContainerId[..Math.Min(12, request.ContainerId.Length)]}", command, 30), ct).ConfigureAwait(false);
        await audit.LogAsync($"Docker{request.Action}", "Container", serverId, request.ContainerId, ct).ConfigureAwait(false);
    }

    public async Task<string> GetContainerLogsAsync(int serverId, DockerContainerLogsRequest request, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidContainerId(request.ContainerId)) return string.Empty;

        var tail = Math.Clamp(request.Tail, 1, 10000);
        var command = $"docker logs --tail {tail} {request.ContainerId}";

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker logs - {request.ContainerId[..Math.Min(12, request.ContainerId.Length)]}", command, 15), ct).ConfigureAwait(false);
        return "Log retrieval task queued.";
    }

    public async Task<List<DockerImageDto>> GetImagesAsync(int serverId, CancellationToken ct = default)
    {
        var images = await repo.GetImagesAsync(serverId, ct).ConfigureAwait(false);
        return images.Select(i => new DockerImageDto
        {
            ImageId = i.ImageId,
            Repository = i.Repository,
            Tag = i.Tag,
            Size = i.Size,
            Created = i.Created
        }).ToList();
    }

    public async Task PullImageAsync(int serverId, DockerPullImageRequest request, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidImageName(request.Image))
            throw new BadRequestException("Invalid image name.");

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker pull - {request.Image}", $"docker pull {request.Image}", 300), ct).ConfigureAwait(false);
    }

    public async Task RemoveImageAsync(int serverId, string imageId, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidImageId(imageId))
            throw new BadRequestException("Invalid image ID.");

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker rmi - {imageId[..Math.Min(12, imageId.Length)]}", $"docker rmi {imageId}", 30), ct).ConfigureAwait(false);
    }

    public async Task<List<DockerComposeStackDto>> GetComposeStacksAsync(int serverId, CancellationToken ct = default)
    {
        var stacks = await repo.GetComposeStacksAsync(serverId, ct).ConfigureAwait(false);
        return stacks.Select(s => new DockerComposeStackDto
        {
            Name = s.Name,
            Status = s.Status,
            ConfigFile = s.ConfigFile,
            RunningCount = s.RunningCount,
            TotalCount = s.TotalCount
        }).ToList();
    }

    public async Task ExecuteComposeActionAsync(int serverId, DockerComposeActionRequest request, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidName(request.StackName))
            throw new BadRequestException("Invalid stack name.");

        var composeCommand = request.Action switch
        {
            DockerComposeAction.Up => "up -d",
            DockerComposeAction.Down => "down",
            DockerComposeAction.Restart => "restart",
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Compose {request.Action} - {request.StackName}", $"docker compose -p {request.StackName} {composeCommand}", 120), ct).ConfigureAwait(false);
    }

    public async Task<List<DockerNetworkDto>> GetNetworksAsync(int serverId, CancellationToken ct = default)
    {
        var networks = await repo.GetNetworksAsync(serverId, ct).ConfigureAwait(false);
        return networks.Select(n => new DockerNetworkDto
        {
            NetworkId = n.NetworkId,
            Name = n.Name,
            Driver = n.Driver,
            Scope = n.Scope
        }).ToList();
    }

    public async Task<List<DockerVolumeDto>> GetVolumesAsync(int serverId, CancellationToken ct = default)
    {
        var volumes = await repo.GetVolumesAsync(serverId, ct).ConfigureAwait(false);
        return volumes.Select(v => new DockerVolumeDto
        {
            Name = v.Name,
            Driver = v.Driver,
            Mountpoint = v.Mountpoint
        }).ToList();
    }

    public async Task PruneAsync(int serverId, DockerPruneRequest request, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (request.Containers) parts.Add("docker container prune -f");
        if (request.Images) parts.Add("docker image prune -af");
        if (request.Volumes) parts.Add("docker volume prune -f");
        if (parts.Count == 0)
            throw new BadRequestException("No prune targets specified.");

        var command = string.Join(" && ", parts);
        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, "Docker prune", command, 120), ct).ConfigureAwait(false);
    }

    public async Task UpdateResourceLimitsAsync(int serverId, DockerResourceLimitsRequest request, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidContainerId(request.ContainerId))
            throw new BadRequestException("Invalid container ID.");

        var parts = new List<string>();
        if (request.CpuLimit > 0) parts.Add($"--cpus={request.CpuLimit.ToString("F2", CultureInfo.InvariantCulture)}");
        if (request.MemoryLimitMb > 0) parts.Add($"--memory={request.MemoryLimitMb}m");
        if (parts.Count == 0)
            throw new BadRequestException("No resource limits specified.");

        var command = $"docker update {string.Join(' ', parts)} {request.ContainerId}";
        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker update limits - {request.ContainerId[..Math.Min(12, request.ContainerId.Length)]}", command, 15), ct).ConfigureAwait(false);
    }

    private static string MapActionToCommand(DockerContainerAction action) => action switch
    {
        DockerContainerAction.Start => "start",
        DockerContainerAction.Stop => "stop",
        DockerContainerAction.Restart => "restart",
        DockerContainerAction.Remove => "rm",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public async Task InspectContainerAsync(int serverId, string containerId, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidContainerId(containerId))
            throw new BadRequestException("Invalid container ID.");

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker inspect - {containerId[..Math.Min(12, containerId.Length)]}", $"docker inspect {containerId}", 15), ct).ConfigureAwait(false);
    }

    public async Task GetComposeFileAsync(int serverId, string stackName, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidName(stackName))
            throw new BadRequestException("Invalid stack name.");

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker compose file - {stackName}", $"docker compose -p {stackName} config", 15), ct).ConfigureAwait(false);
    }

    public async Task SaveComposeFileAsync(int serverId, DockerComposeFileSaveRequest request, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidName(request.StackName))
            throw new BadRequestException("Invalid stack name.");
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new BadRequestException("Content is required.");

        var decoded = DockerCommandHelper.EncodeBase64Shell(request.Content);
        var command = $"{decoded} | tee /tmp/compose-{request.StackName}.yml && docker compose -f /tmp/compose-{request.StackName}.yml -p {request.StackName} up -d";

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker compose deploy - {request.StackName}", command, 120), ct).ConfigureAwait(false);
    }

    public async Task ExecuteShellCommandAsync(int serverId, DockerExecRequest request, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidContainerId(request.ContainerId))
            throw new BadRequestException("Invalid container ID.");
        if (string.IsNullOrWhiteSpace(request.Command))
            throw new BadRequestException("Command is required.");

        var decoded = DockerCommandHelper.EncodeBase64Shell(request.Command);
        var command = $"{decoded} | docker exec -i {request.ContainerId} sh";

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker exec - {request.ContainerId[..Math.Min(12, request.ContainerId.Length)]}", command, 30), ct).ConfigureAwait(false);
        await audit.LogAsync("DockerExec", "Server", serverId, request.ContainerId[..Math.Min(12, request.ContainerId.Length)], ct).ConfigureAwait(false);
    }

    public async Task GetContainerEnvVarsAsync(int serverId, string containerId, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidContainerId(containerId))
            throw new BadRequestException("Invalid container ID.");

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker env - {containerId[..Math.Min(12, containerId.Length)]}", $"docker inspect --format='{{{{json .Config.Env}}}}' {containerId}", 15), ct).ConfigureAwait(false);
    }

    public async Task ListContainerFilesAsync(int serverId, DockerBrowseRequest request, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidContainerId(request.ContainerId))
            throw new BadRequestException("Invalid container ID.");
        if (!DockerIdValidator.IsValidContainerPath(request.Path))
            throw new BadRequestException("Invalid container path.");

        // Hardening (High #9): pass path as a positional argument after `--` so it can never be
        // interpreted as an `ls` option, and avoid wrapping the whole thing in `sh -c`.
        var command = $"docker exec {request.ContainerId} ls -la -- {request.Path}";

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker ls - {request.ContainerId[..Math.Min(12, request.ContainerId.Length)]}", command, 15), ct).ConfigureAwait(false);
    }

    public async Task BuildImageAsync(int serverId, DockerBuildRequest request, CancellationToken ct = default)
    {
        if (!DockerIdValidator.IsValidImageName(request.ImageTag))
            throw new BadRequestException("Invalid image tag.");
        if (string.IsNullOrWhiteSpace(request.DockerfileContent))
            throw new BadRequestException("Dockerfile content is required.");

        var decoded = DockerCommandHelper.EncodeBase64Shell(request.DockerfileContent);
        var command = $"{decoded} | tee /tmp/Dockerfile.build && docker build -t {request.ImageTag} -f /tmp/Dockerfile.build .";

        await QueueTaskAsync(ServerTaskFactory.Shell(serverId, $"Docker build - {request.ImageTag}", command, 600), ct).ConfigureAwait(false);
    }
}
