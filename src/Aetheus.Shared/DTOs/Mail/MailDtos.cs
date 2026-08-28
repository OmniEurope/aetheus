// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

// --- Aggregate heartbeat data ---

public sealed record MailDataDto
{
    public bool IsInstalled { get; init; }
    public bool IsPostfixRunning { get; init; }
    public bool IsDovecotRunning { get; init; }
    [StringLength(100)]
    public string PostfixVersion { get; init; } = string.Empty;
    [StringLength(100)]
    public string DovecotVersion { get; init; } = string.Empty;
    public int QueueSize { get; init; }
    public List<MailDomainDto> Domains { get; init; } = [];
    public List<MailAccountDto> Accounts { get; init; } = [];
}

// --- Domain info ---

public sealed record MailDomainDto
{
    public int Id { get; init; }
    [StringLength(253)]
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    [StringLength(63)]
    public string DkimSelector { get; init; } = "default";
    public bool HasSpf { get; init; }
    public bool HasDkim { get; init; }
    public bool HasDmarc { get; init; }
    public DateTime CreatedAt { get; init; }
}

// --- Account info ---

public sealed record MailAccountDto
{
    public int Id { get; init; }
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;
    [StringLength(253)]
    public string Domain { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public int QuotaMb { get; init; }
    public int UsedMb { get; init; }
    public DateTime CreatedAt { get; init; }
}

// --- Queue item ---

public sealed record MailQueueItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Sender { get; init; } = string.Empty;
    public string Recipient { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTime ArrivalTime { get; init; }
    public string Status { get; init; } = string.Empty;
}

// --- Action request ---

public sealed record MailActionRequest
{
    [Required]
    public Shared.Enums.MailAction Action { get; init; }
}

// --- Domain requests ---

public sealed record CreateMailDomainRequest
{
    [Required]
    [StringLength(253, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*\.[a-zA-Z]{2,}$")]
    public string Name { get; init; } = string.Empty;

    [StringLength(63, MinimumLength = 1)]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$")]
    public string DkimSelector { get; init; } = "default";
}

public sealed record UpdateMailDomainRequest
{
    public bool? IsActive { get; init; }

    [StringLength(63, MinimumLength = 1)]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$")]
    public string? DkimSelector { get; init; }
}

// --- Account requests ---

public sealed record CreateMailAccountRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254, MinimumLength = 5)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(PasswordPolicy.MaximumLength, MinimumLength = PasswordPolicy.MinimumLength)]
    public string Password { get; init; } = string.Empty;

    [Range(0, 102400)]
    public int QuotaMb { get; init; } = 1024;
}

public sealed record UpdateMailAccountRequest
{
    public bool? IsActive { get; init; }

    [Range(0, 102400)]
    public int? QuotaMb { get; init; }

    [StringLength(PasswordPolicy.MaximumLength, MinimumLength = PasswordPolicy.MinimumLength)]
    public string? NewPassword { get; init; }
}

// --- Log request ---

public sealed record MailLogRequest
{
    [Required]
    [StringLength(20)]
    [RegularExpression("^(postfix|dovecot)$")]
    public string LogType { get; init; } = "postfix";

    [Range(1, 10000)]
    public int Lines { get; init; } = 100;
}

// --- Setup wizard request ---

public sealed record MailSetupRequest
{
    [Required]
    [StringLength(253, MinimumLength = 3)]
    public string Hostname { get; init; } = string.Empty;

    [Required]
    [StringLength(253, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*\.[a-zA-Z]{2,}$")]
    public string Domain { get; init; } = string.Empty;

    [StringLength(63, MinimumLength = 1)]
    public string DkimSelector { get; init; } = "default";

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string AdminEmail { get; init; } = string.Empty;

    [Required]
    [StringLength(PasswordPolicy.MaximumLength, MinimumLength = PasswordPolicy.MinimumLength)]
    public string AdminPassword { get; init; } = string.Empty;

    [Range(0, 102400)]
    public int QuotaMb { get; init; } = 1024;
}

// --- DNS records response ---

public sealed record MailDnsRecordsDto
{
    public string Domain { get; init; } = string.Empty;
    public string SpfRecord { get; init; } = string.Empty;
    public string DkimRecord { get; init; } = string.Empty;
    public string DkimSelector { get; init; } = string.Empty;
    public string DkimPublicKey { get; init; } = string.Empty;
    public string DmarcRecord { get; init; } = string.Empty;
    public string MxRecord { get; init; } = string.Empty;
}

// --- Mail alias ---

public sealed record MailAliasDto
{
    public int Id { get; init; }
    public string SourceEmail { get; init; } = string.Empty;
    public string DestinationEmail { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public DateTime CreatedAt { get; init; }
}

public sealed record CreateMailAliasRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254, MinimumLength = 5)]
    public string SourceEmail { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254, MinimumLength = 5)]
    public string DestinationEmail { get; init; } = string.Empty;
}

public sealed record UpdateMailAliasRequest
{
    public bool? IsActive { get; init; }

    [EmailAddress]
    [StringLength(254, MinimumLength = 5)]
    public string? DestinationEmail { get; init; }
}

// --- Mailbox quota usage ---

public sealed record MailboxQuotaDto
{
    public int AccountId { get; init; }
    public string Email { get; init; } = string.Empty;
    public int QuotaMb { get; init; }
    public int UsedMb { get; init; }
    public double UsagePercent => QuotaMb > 0 ? Math.Round(UsedMb * 100.0 / QuotaMb, 1) : 0;
}

// --- SpamAssassin configuration ---

public sealed record SpamFilterConfigDto
{
    public bool IsInstalled { get; init; }
    public bool IsRunning { get; init; }
    public string Version { get; init; } = string.Empty;
    public double RequiredScore { get; init; } = 5.0;
    public bool RewriteHeader { get; init; } = true;
    public List<string> WhitelistedAddresses { get; init; } = [];
    public List<string> BlacklistedAddresses { get; init; } = [];
}

public sealed record UpdateSpamFilterRequest
{
    [Range(0.1, 100.0)]
    public double? RequiredScore { get; init; }

    public bool? RewriteHeader { get; init; }

    [MaxLength(1000)]
    [MaxItemStringLength(254)]
    public List<string>? WhitelistedAddresses { get; init; }

    [MaxLength(1000)]
    [MaxItemStringLength(254)]
    public List<string>? BlacklistedAddresses { get; init; }
}

// --- Mail SSL/TLS certificates ---

public sealed record MailCertificateDto
{
    public int Id { get; init; }
    public string Domain { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public bool IsValid { get; init; }
    public bool AutoRenew { get; init; } = true;
}

public sealed record RequestMailCertificateRequest
{
    [Required]
    [StringLength(253, MinimumLength = 3)]
    public string Domain { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;
}

// --- DKIM key rotation ---

public sealed record DkimRotationRequest
{
    [Required]
    [StringLength(63, MinimumLength = 1)]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$")]
    public string NewSelector { get; init; } = string.Empty;
}

public sealed record DkimRotationResultDto
{
    public string Domain { get; init; } = string.Empty;
    public string OldSelector { get; init; } = string.Empty;
    public string NewSelector { get; init; } = string.Empty;
    public string DnsRecordName { get; init; } = string.Empty;
    public string DnsRecordValue { get; init; } = string.Empty;
}
