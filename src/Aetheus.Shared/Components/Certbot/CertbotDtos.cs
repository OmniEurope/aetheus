// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.Certbot;

// --- Aggregate heartbeat data ---

public sealed record CertbotDataDto
{
    public bool IsInstalled { get; init; }
    [StringLength(100)]
    public string Version { get; init; } = string.Empty;
    [MaxLength(2048)]
    public List<CertbotCertificateDto> Certificates { get; init; } = [];
    // Outcome of the last renewal rehearsal (`certbot renew --dry-run`, weekly timer or on demand).
    // Null when none was recorded yet.
    public DateTime? RenewalCheckedAt { get; init; }
    public bool? RenewalCheckSucceeded { get; init; }
}

// --- Certificate info ---

public sealed record CertbotCertificateDto
{
    [StringLength(255)]
    public string Name { get; init; } = string.Empty;
    [MaxLength(100)]
    [Aetheus.Shared.Components.Shared.MaxItemStringLength(253)]
    public List<string> Domains { get; init; } = [];
    public DateTime ExpiryDate { get; init; }
    [StringLength(4096)]
    public string CertPath { get; init; } = string.Empty;
    [StringLength(4096)]
    public string KeyPath { get; init; } = string.Empty;
    // How this lineage renews, read from /etc/letsencrypt/renewal/<name>.conf (PLAN-007).
    [StringLength(100)]
    public string Authenticator { get; init; } = string.Empty;
    [StringLength(4096)]
    public string WebrootPath { get; init; } = string.Empty;
    public Aetheus.Shared.Components.Certbot.CertbotRenewalConvention RenewalConvention { get; init; }
    // "Expiring soon" is intentionally NOT a computed DTO property: it depends on the current
    // time, not on the payload, so it is evaluated by the consumer against ExpiryDate (the
    // front uses browser-local DateTime.Now). Keeping it here would bake server UtcNow into
    // the serialized contract.
}

// --- Action request ---

public sealed record CertbotActionRequest
{
    [Required]
    public Aetheus.Shared.Components.Certbot.CertbotAction Action { get; init; }

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
