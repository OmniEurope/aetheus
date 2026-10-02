// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.ServerConfigurations;

public sealed record ServerConfigYaml
{
    public ServerConfigServerSection Server { get; init; } = new();
    public ServerConfigDockerSection? Docker { get; init; }
    public ServerConfigServicesSection? Services { get; init; }
}

public sealed record ServerConfigServerSection
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = "Normal";
    public List<string> Tags { get; init; } = [];
}

public sealed record ServerConfigDockerSection
{
    public List<ServerConfigContainer> Containers { get; init; } = [];
    public List<ServerConfigComposeStack> ComposeStacks { get; init; } = [];
    public List<string> Images { get; init; } = [];
}

public sealed record ServerConfigContainer
{
    public string Image { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public List<string> Ports { get; init; } = [];
    public List<string> Volumes { get; init; } = [];
    public string Restart { get; init; } = string.Empty;
    public Dictionary<string, string> Env { get; init; } = [];
}

public sealed record ServerConfigComposeStack
{
    public string Name { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}

public sealed record ServerConfigServicesSection
{
    public List<ServerConfigService> Systemd { get; init; } = [];
}

public sealed record ServerConfigService
{
    public string Name { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

public sealed record ServerConfigValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = [];
}

public sealed record ServerConfigPreviewDto
{
    public string ServerName { get; init; } = string.Empty;
    [MaxLength(2048)]
    public List<ServerConfigChange> Changes { get; init; } = [];
    public int TaskCount { get; init; }
}

public sealed record ServerConfigChange
{
    public string Category { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed record ServerConfigDeployResultDto
{
    public int TasksCreated { get; init; }
    public List<string> TaskNames { get; init; } = [];
}

public sealed record ServerConfigImportRequest
{
    [Required]
    [StringLength(50000)]
    public string Yaml { get; init; } = string.Empty;
}
