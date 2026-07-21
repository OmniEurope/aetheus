// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using System.Text.RegularExpressions;
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aetheus.Back.Components.ServerConfigurations;

public partial class ServerConfigurationService(IServerConfigurationRepository repo, TimeProvider timeProvider, ITaskService taskService) : IServerConfigurationService
{
    // Persist a queued task AND push the "TaskQueued" SignalR event so the top-bar tracker shows it
    // live (and can later flip it Running/Completed). This repo's AddTaskAsync carries no CancellationToken.
    private async Task QueueTaskAsync(ServerTask task)
    {
        await repo.AddTaskAsync(task).ConfigureAwait(false);
        await taskService.NotifyTaskQueuedAsync(task).ConfigureAwait(false);
    }

    // Hardening (Critical #2): every YAML field that ends up in a shell command is validated
    // against one of these strict allow-list regexes before deployment. Anything that doesn't
    // match is rejected during validation - preventing shell-injection via crafted YAML imports.
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-:/@]{0,255}$")]
    private static partial Regex DockerImageRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-]{0,63}$")]
    private static partial Regex DockerNameRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_\-]{0,63}$")]
    private static partial Regex StackNameRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-@]{0,63}$")]
    private static partial Regex ServiceNameRegex();

    [GeneratedRegex(@"^\d{1,5}(:\d{1,5})?(/(tcp|udp))?$")]
    private static partial Regex PortMappingRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9_./\-]+:[a-zA-Z0-9_./\-]+(:ro|:rw)?$")]
    private static partial Regex VolumeMappingRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex EnvKeyRegex();

    // Env values may contain a wide range of characters - but never shell metacharacters.
    [GeneratedRegex(@"[;|&`$()<>\\""'\r\n\0]")]
    private static partial Regex ShellMetaInValueRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9 _.\-]{0,63}$")]
    private static partial Regex ServerNameRegex();

    [GeneratedRegex(@"^(always|on-failure|unless-stopped|no)$")]
    private static partial Regex RestartPolicyRegex();
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    public async Task<string?> ExportConfigurationAsync(int serverId, CancellationToken ct = default)
    {
        var server = await repo.GetServerWithDockerAndServicesReadOnlyAsync(serverId, ct).ConfigureAwait(false);

        if (server is null)
            return null;

        var config = new ServerConfigYaml
        {
            Server = new ServerConfigServerSection
            {
                Name = server.Name,
                Type = server.Type.ToString(),
                Tags = TagsHelper.DeserializeTags(server.Tags)
            },
            Docker = BuildDockerSection(server),
            Services = BuildServicesSection(server)
        };

        return YamlSerializer.Serialize(config);
    }

    public Task<ServerConfigValidationResult> ValidateConfigurationAsync(string yaml, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(yaml))
        {
            errors.Add("YAML content is empty.");
            return Task.FromResult(new ServerConfigValidationResult { IsValid = false, Errors = errors });
        }

        ServerConfigYaml config;
        try
        {
            config = YamlParsingHelper.Deserializer.Deserialize<ServerConfigYaml>(yaml);
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidOperationException or ArgumentException)
        {
            errors.Add($"Invalid YAML syntax: {ex.Message}");
            return Task.FromResult(new ServerConfigValidationResult { IsValid = false, Errors = errors });
        }

        if (config.Server is null)
            errors.Add("Missing required 'server' section.");

        if (string.IsNullOrWhiteSpace(config.Server?.Name))
            errors.Add("Server name is required.");
        else if (!ServerNameRegex().IsMatch(config.Server.Name))
            errors.Add($"Server name '{config.Server.Name}' contains invalid characters.");

        if (config.Server is not null && !Enum.TryParse<ServerType>(config.Server.Type, true, out _))
            errors.Add($"Invalid server type: '{config.Server.Type}'. Valid: Normal, Build, Docker.");

        if (config.Docker?.Containers is not null)
        {
            foreach (var container in config.Docker.Containers)
            {
                if (string.IsNullOrWhiteSpace(container.Image))
                    errors.Add($"Container '{container.Name}' is missing an image.");
                else if (!DockerImageRegex().IsMatch(container.Image))
                    errors.Add($"Container image '{container.Image}' contains invalid characters.");

                if (string.IsNullOrWhiteSpace(container.Name))
                    errors.Add("Container name is required.");
                else if (!DockerNameRegex().IsMatch(container.Name))
                    errors.Add($"Container name '{container.Name}' contains invalid characters.");

                if (!string.IsNullOrWhiteSpace(container.Restart) && !RestartPolicyRegex().IsMatch(container.Restart))
                    errors.Add($"Container '{container.Name}' has invalid restart policy '{container.Restart}'.");

                foreach (var port in container.Ports)
                    if (!PortMappingRegex().IsMatch(port))
                        errors.Add($"Container '{container.Name}' has invalid port mapping '{port}'.");

                foreach (var vol in container.Volumes)
                    if (!VolumeMappingRegex().IsMatch(vol))
                        errors.Add($"Container '{container.Name}' has invalid volume mapping '{vol}'.");

                foreach (var (k, v) in container.Env)
                {
                    if (!EnvKeyRegex().IsMatch(k))
                        errors.Add($"Container '{container.Name}' has invalid env key '{k}'.");
                    if (v is not null && ShellMetaInValueRegex().IsMatch(v))
                        errors.Add($"Container '{container.Name}' env '{k}' contains shell metacharacters.");
                }
            }
        }

        if (config.Docker?.Images is not null)
        {
            foreach (var image in config.Docker.Images)
                if (!DockerImageRegex().IsMatch(image))
                    errors.Add($"Docker image '{image}' contains invalid characters.");
        }

        if (config.Docker?.ComposeStacks is not null)
        {
            foreach (var stack in config.Docker.ComposeStacks)
            {
                if (string.IsNullOrWhiteSpace(stack.Name))
                    errors.Add("Compose stack name is required.");
                else if (!StackNameRegex().IsMatch(stack.Name))
                    errors.Add($"Compose stack name '{stack.Name}' contains invalid characters.");

                if (string.IsNullOrWhiteSpace(stack.Content))
                    errors.Add($"Compose stack '{stack.Name}' is missing content.");
            }
        }

        if (config.Services?.Systemd is not null)
        {
            foreach (var svc in config.Services.Systemd)
            {
                if (string.IsNullOrWhiteSpace(svc.Name))
                    errors.Add("Systemd service name is required.");
                else if (!ServiceNameRegex().IsMatch(svc.Name))
                    errors.Add($"Systemd service name '{svc.Name}' contains invalid characters.");
            }
        }

        return Task.FromResult(new ServerConfigValidationResult { IsValid = errors.Count == 0, Errors = errors });
    }

    public async Task<ServerConfigPreviewDto?> PreviewImportAsync(int serverId, string yaml, CancellationToken ct = default)
    {
        var validation = await ValidateConfigurationAsync(yaml, ct).ConfigureAwait(false);
        if (!validation.IsValid)
            return null;

        var config = YamlParsingHelper.Deserializer.Deserialize<ServerConfigYaml>(yaml);

        var server = await repo.GetServerWithDockerAndServicesReadOnlyAsync(serverId, ct).ConfigureAwait(false);

        if (server is null)
            return null;

        var changes = ComputeChanges(server, config);

        return new ServerConfigPreviewDto
        {
            ServerName = server.Name,
            Changes = changes,
            TaskCount = changes.Count(c => c.Action is not "unchanged")
        };
    }

    public async Task<ServerConfigDeployResultDto?> DeployConfigurationAsync(int serverId, string yaml, CancellationToken ct = default)
    {
        var validation = await ValidateConfigurationAsync(yaml, ct).ConfigureAwait(false);
        if (!validation.IsValid)
            return null;

        var config = YamlParsingHelper.Deserializer.Deserialize<ServerConfigYaml>(yaml);

        var server = await repo.GetServerWithDockerAndServicesAsync(serverId, ct).ConfigureAwait(false);

        if (server is null)
            return null;

        // Update server metadata
        if (!string.IsNullOrWhiteSpace(config.Server.Name))
            server.Name = config.Server.Name;
        if (Enum.TryParse<ServerType>(config.Server.Type, true, out var serverType))
            server.Type = serverType;
        server.Tags = JsonSerializer.Serialize(config.Server.Tags ?? []);
        server.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        var taskNames = new List<string>();

        // Docker images to pull
        if (config.Docker?.Images is not null)
        {
            var existingImages = server.DockerImages
                .Select(i => $"{i.Repository}:{i.Tag}".TrimEnd(':'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var image in config.Docker.Images)
            {
                if (!existingImages.Contains(image))
                {
                    var taskName = $"Pull image - {image}";
                    await QueueTaskAsync(ServerTaskFactory.Shell(serverId, taskName, $"docker pull {image}", 300));
                    taskNames.Add(taskName);
                }
            }
        }

        // Docker containers to create
        if (config.Docker?.Containers is not null)
        {
            var existingContainers = server.DockerContainers
                .Select(c => c.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var container in config.Docker.Containers)
            {
                if (!existingContainers.Contains(container.Name))
                {
                    var command = BuildDockerRunCommand(container);
                    var taskName = $"Create container - {container.Name}";
                    await QueueTaskAsync(ServerTaskFactory.Shell(serverId, taskName, command, 120));
                    taskNames.Add(taskName);
                }
            }
        }

        // Compose stacks to deploy
        if (config.Docker?.ComposeStacks is not null)
        {
            var existingStacks = server.DockerComposeStacks
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var stack in config.Docker.ComposeStacks)
            {
                var taskName = existingStacks.Contains(stack.Name)
                    ? $"Update compose - {stack.Name}"
                    : $"Deploy compose - {stack.Name}";

                // No generic WriteFile OperationKind exists (only the Apache-specific ApacheSaveConfig),
                // so per audit-360 C-1 we keep the printf-in-shell path but make the single-quote
                // escaping bulletproof: the content is wrapped in single quotes (which suppress ALL
                // shell expansion - $, `, \, etc.), and each embedded ' is closed-escaped-reopened as
                // '\'' so the string cannot break out of the quoting. A NUL byte cannot survive a
                // C-string argv anyway, so reject it outright rather than silently truncating the file.
                if (stack.Content.Contains('\0'))
                    throw new BadRequestException($"Compose stack '{stack.Name}' content contains a NUL byte.");
                var escapedContent = stack.Content.Replace("'", "'\\''");
                var command = $"mkdir -p /opt/compose/{stack.Name} && printf '%s' '{escapedContent}' > /opt/compose/{stack.Name}/docker-compose.yml && cd /opt/compose/{stack.Name} && docker compose up -d";

                await QueueTaskAsync(ServerTaskFactory.Shell(serverId, taskName, command, 120));
                taskNames.Add(taskName);
            }
        }

        // Systemd services to enable (Phase 3, option B). Migrated from a free-form
        // `sudo systemctl enable --now <unit>` shell task - which CommandValidator/sudoers never
        // actually permitted (systemctl enable is forbidden outside the controlled recipe) - to a
        // typed ServiceEnable operation. The agent runs `sudo -n systemctl enable --now <unit>`
        // against the argv-exact, fixed-unit /etc/sudoers.d/aetheus-service-enable allow-list.
        if (config.Services?.Systemd is not null)
        {
            foreach (var svc in config.Services.Systemd.Where(s => s.Enabled))
            {
                var taskName = $"Enable service - {svc.Name}";
                await QueueTaskAsync(ServerTaskFactory.Operation(serverId, taskName, OperationKind.ServiceEnable, svc.Name, 30));
                taskNames.Add(taskName);
            }
        }

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        return new ServerConfigDeployResultDto
        {
            TasksCreated = taskNames.Count,
            TaskNames = taskNames
        };
    }

    private static ServerConfigDockerSection? BuildDockerSection(Server server)
    {
        if (server.DockerContainers.Count == 0 &&
            server.DockerImages.Count == 0 &&
            server.DockerComposeStacks.Count == 0)
            return null;

        return new ServerConfigDockerSection
        {
            Containers = server.DockerContainers.Select(c => new ServerConfigContainer
            {
                Image = c.Image,
                Name = c.Name,
                Ports = string.IsNullOrWhiteSpace(c.Ports) ? [] : [.. c.Ports.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)],
                Restart = "always"
            }).ToList(),
            ComposeStacks = server.DockerComposeStacks.Select(s => new ServerConfigComposeStack
            {
                Name = s.Name,
                Content = s.ConfigFile
            }).ToList(),
            Images = server.DockerImages
                .Select(i => string.IsNullOrWhiteSpace(i.Tag) || i.Tag == "<none>"
                    ? i.Repository
                    : $"{i.Repository}:{i.Tag}")
                .Where(name => !string.IsNullOrWhiteSpace(name) && name != "<none>")
                .Distinct()
                .ToList()
        };
    }

    private static ServerConfigServicesSection? BuildServicesSection(Server server)
    {
        var systemdServices = server.Services
            .Where(s => s.Type == ServiceType.Systemd)
            .Select(s => new ServerConfigService { Name = s.Name, Enabled = s.IsRunning })
            .ToList();

        if (systemdServices.Count == 0)
            return null;

        return new ServerConfigServicesSection { Systemd = systemdServices };
    }

    private static List<ServerConfigChange> ComputeChanges(Server server, ServerConfigYaml config)
    {
        var changes = new List<ServerConfigChange>();

        // Server metadata
        if (!string.Equals(server.Name, config.Server.Name, StringComparison.Ordinal))
            changes.Add(new ServerConfigChange { Category = "Server", Action = "update", Description = $"Rename '{server.Name}' → '{config.Server.Name}'" });

        if (Enum.TryParse<ServerType>(config.Server.Type, true, out var newType) && server.Type != newType)
            changes.Add(new ServerConfigChange { Category = "Server", Action = "update", Description = $"Change type {server.Type} → {newType}" });

        // Docker images
        if (config.Docker?.Images is not null)
        {
            var existingImages = server.DockerImages
                .Select(i => $"{i.Repository}:{i.Tag}".TrimEnd(':'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var image in config.Docker.Images)
            {
                changes.Add(existingImages.Contains(image)
                    ? new ServerConfigChange { Category = "Docker Image", Action = "unchanged", Description = image }
                    : new ServerConfigChange { Category = "Docker Image", Action = "pull", Description = image });
            }
        }

        // Docker containers
        if (config.Docker?.Containers is not null)
        {
            var existingContainers = server.DockerContainers
                .Select(c => c.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var container in config.Docker.Containers)
            {
                changes.Add(existingContainers.Contains(container.Name)
                    ? new ServerConfigChange { Category = "Docker Container", Action = "unchanged", Description = container.Name }
                    : new ServerConfigChange { Category = "Docker Container", Action = "create", Description = $"{container.Name} ({container.Image})" });
            }
        }

        // Compose stacks
        if (config.Docker?.ComposeStacks is not null)
        {
            var existingStacks = server.DockerComposeStacks
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var stack in config.Docker.ComposeStacks)
            {
                changes.Add(existingStacks.Contains(stack.Name)
                    ? new ServerConfigChange { Category = "Compose Stack", Action = "update", Description = stack.Name }
                    : new ServerConfigChange { Category = "Compose Stack", Action = "deploy", Description = stack.Name });
            }
        }

        // Systemd services
        if (config.Services?.Systemd is not null)
        {
            var existingServices = server.Services
                .Where(s => s.Type == ServiceType.Systemd)
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var svc in config.Services.Systemd)
            {
                changes.Add(existingServices.Contains(svc.Name)
                    ? new ServerConfigChange { Category = "Systemd Service", Action = "unchanged", Description = svc.Name }
                    : new ServerConfigChange { Category = "Systemd Service", Action = "enable", Description = svc.Name });
            }
        }

        return changes;
    }

    private static string BuildDockerRunCommand(ServerConfigContainer container)
    {
        // Defense-in-depth (audit-360 C-1): re-validate every interpolated field against the
        // SAME allow-list regexes ValidateConfigurationAsync applies, so the builder cannot emit
        // an injectable `docker run` string even if called without a prior validation pass.
        if (!DockerNameRegex().IsMatch(container.Name))
            throw new BadRequestException($"Container name '{container.Name}' contains invalid characters.");
        if (!DockerImageRegex().IsMatch(container.Image))
            throw new BadRequestException($"Container image '{container.Image}' contains invalid characters.");
        if (!string.IsNullOrWhiteSpace(container.Restart) && !RestartPolicyRegex().IsMatch(container.Restart))
            throw new BadRequestException($"Container '{container.Name}' has invalid restart policy '{container.Restart}'.");

        var parts = new List<string> { "docker run -d" };
        parts.Add($"--name {container.Name}");

        if (!string.IsNullOrWhiteSpace(container.Restart))
            parts.Add($"--restart {container.Restart}");

        foreach (var port in container.Ports)
        {
            if (!PortMappingRegex().IsMatch(port))
                throw new BadRequestException($"Container '{container.Name}' has invalid port mapping '{port}'.");
            parts.Add($"-p {port}");
        }

        foreach (var volume in container.Volumes)
        {
            if (!VolumeMappingRegex().IsMatch(volume))
                throw new BadRequestException($"Container '{container.Name}' has invalid volume mapping '{volume}'.");
            parts.Add($"-v {volume}");
        }

        foreach (var (key, value) in container.Env)
        {
            if (!EnvKeyRegex().IsMatch(key))
                throw new BadRequestException($"Container '{container.Name}' has invalid env key '{key}'.");
            if (ShellMetaInValueRegex().IsMatch(value))
                throw new BadRequestException($"Container '{container.Name}' env '{key}' contains shell metacharacters.");
            // Single-quote the value so an embedded space cannot word-split into a stray docker-run
            // argument. Safe without escaping: ShellMetaInValueRegex already rejects single quotes.
            parts.Add($"-e {key}='{value}'");
        }

        parts.Add(container.Image);
        return string.Join(" ", parts);
    }
}
