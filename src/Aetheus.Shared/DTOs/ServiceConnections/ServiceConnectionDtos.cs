// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

public abstract record ServiceConnectionView
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

public sealed record ServiceConnectionDto : ServiceConnectionView;

public sealed record ServiceConnectionDetailDto : ServiceConnectionView
{
    public string ConfigurationJson { get; init; } = "{}";
}

public abstract record ServiceConnectionRequest
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

public sealed record CreateServiceConnectionRequest : ServiceConnectionRequest
{
    public ServiceConnectionType Type { get; init; }
}

public sealed record UpdateServiceConnectionRequest : ServiceConnectionRequest;

public sealed record ServiceConnectionTestResultDto
{
    public ServiceConnectionTestStatus Status { get; init; }
    public string? Message { get; init; }
}
