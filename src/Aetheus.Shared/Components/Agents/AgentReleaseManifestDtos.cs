// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Components.Agents;

public sealed record AgentReleaseManifestDto
{
    [Required, StringLength(50)]
    public string SoftwareVersion { get; init; } = string.Empty;
    [Range(1, int.MaxValue)]
    public int ProtocolVersion { get; init; }
    [Range(1, int.MaxValue)]
    public int MinimumSupportedProtocol { get; init; }
    [Range(1, int.MaxValue)]
    public int MaximumSupportedProtocol { get; init; }
    [MaxLength(128)]
    public List<string> SoftwareCapabilities { get; init; } = [];
    [MaxLength(16)]
    public List<AgentReleaseArchiveDto> Archives { get; init; } = [];
    public DateTime ProducedAtUtc { get; init; }
    [Required, StringLength(64)]
    public string Commit { get; init; } = string.Empty;
}

public sealed record AgentReleaseArchiveDto
{
    [Required, StringLength(32)]
    public string Platform { get; init; } = string.Empty;
    [Required, StringLength(32)]
    public string Architecture { get; init; } = string.Empty;
    [Required, StringLength(255)]
    public string FileName { get; init; } = string.Empty;
    [Range(1, long.MaxValue)]
    public long SizeBytes { get; init; }
    [Required, RegularExpression("^[a-fA-F0-9]{64}$")]
    public string Sha256 { get; init; } = string.Empty;
}
