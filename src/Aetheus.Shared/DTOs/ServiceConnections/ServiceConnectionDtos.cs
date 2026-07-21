// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

public sealed record ServiceConnectionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public ServiceConnectionType Type { get; init; }
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public string? Url { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record ServiceConnectionDetailDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public ServiceConnectionType Type { get; init; }
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public string? Url { get; init; }
    public string ConfigurationJson { get; init; } = "{}";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record CreateServiceConnectionRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public ServiceConnectionType Type { get; init; }

    public int? ProjectId { get; init; }

    [StringLength(500)]
    public string? Url { get; init; }

    [Required]
    [StringLength(8000)]
    public string ConfigurationJson { get; init; } = "{}";
}

public sealed record UpdateServiceConnectionRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public int? ProjectId { get; init; }

    [StringLength(500)]
    public string? Url { get; init; }

    [Required]
    [StringLength(8000)]
    public string ConfigurationJson { get; init; } = "{}";
}

public sealed record ServiceConnectionTestResultDto
{
    public ServiceConnectionTestStatus Status { get; init; }
    public string? Message { get; init; }
}
