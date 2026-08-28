// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.DTOs;

// --- Aggregate heartbeat data ---

public sealed record CertbotDataDto
{
    public bool IsInstalled { get; init; }
    [StringLength(100)]
    public string Version { get; init; } = string.Empty;
    [MaxLength(2048)]
    public List<CertbotCertificateDto> Certificates { get; init; } = [];
}

// --- Certificate info ---

public sealed record CertbotCertificateDto
{
    [StringLength(255)]
    public string Name { get; init; } = string.Empty;
    [MaxLength(100)]
    [Aetheus.Shared.Validation.MaxItemStringLength(253)]
    public List<string> Domains { get; init; } = [];
    public DateTime ExpiryDate { get; init; }
    [StringLength(4096)]
    public string CertPath { get; init; } = string.Empty;
    [StringLength(4096)]
    public string KeyPath { get; init; } = string.Empty;
    // "Expiring soon" is intentionally NOT a computed DTO property: it depends on the current
    // time, not on the payload, so it is evaluated by the consumer against ExpiryDate (the
    // front uses browser-local DateTime.Now). Keeping it here would bake server UtcNow into
    // the serialized contract.
}

// --- Action request ---

public sealed record CertbotActionRequest
{
    [Required]
    public Shared.Enums.CertbotAction Action { get; init; }

    [StringLength(200)]
    public string? CertificateName { get; init; }
}

// --- Create request ---

public sealed record CertbotCreateRequest
{
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Domains { get; init; } = string.Empty;

    [EmailAddress]
    [StringLength(200)]
    public string? Email { get; init; }

    public bool Standalone { get; init; }

    [StringLength(500)]
    public string? WebrootPath { get; init; }
}
