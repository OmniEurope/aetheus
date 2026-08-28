// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs;

/// <summary>
/// Request to attach an external Git repository to a project as its mirror-backed source
/// (Features:ExternalRepos). Credentials are write-only here - never echoed back in <see cref="ExternalRepoDto"/>.
/// </summary>
/// <remarks>
/// Pipeline-clone prerequisite: once attached, the project's <c>RepositoryUrl</c> points at the internal
/// mirror over the backend's smart-HTTP path, which requires HTTP Basic auth. A pipeline that clones it
/// must supply <c>GIT_USERNAME</c> / <c>GIT_PASSWORD</c> variables (these are NOT injected automatically);
/// otherwise the clone step fails with a 401. The external credential supplied here never reaches the
/// agent - it is used only by the backend to refresh the mirror.
/// </remarks>
public sealed record AttachExternalRepoRequest : GitRepositoryCoordinatesRequest
{
    /// <summary>Required for self-hosted providers (Gitea / self-hosted GitLab); ignored for SaaS defaults.</summary>
    [StringLength(500)]
    public string? BaseUrl { get; init; }

    [StringLength(200)]
    public string? DefaultBranch { get; init; }

    public GitAuthType AuthType { get; init; }

    [StringLength(200)]
    public string? Username { get; init; }

    [StringLength(4000)]
    public string? Token { get; init; }

    [StringLength(20000)]
    public string? PrivateKeyPem { get; init; }

    [StringLength(500)]
    public string? Passphrase { get; init; }

    [StringLength(8000)]
    public string? KnownHosts { get; init; }

    [Range(1, 1440)]
    public int FetchIntervalMinutes { get; init; } = 15;

    /// <summary>Push-back to the external remote - explicit opt-in (Phase 4).</summary>
    public bool WriteEnabled { get; init; }
}

/// <summary>
/// Read view of a project's external repository source. Carries no secrets.
/// </summary>
public sealed record ExternalRepoDto
{
    public int GitConnectionId { get; init; }
    public int ProjectId { get; init; }
    public GitProviderType ProviderType { get; init; }
    public string OwnerOrGroup { get; init; } = string.Empty;
    public string RepositoryName { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }
    public GitAuthType AuthType { get; init; }
    public GitMirrorStatus MirrorStatus { get; init; }
    public DateTime? LastFetchedAt { get; init; }
    public string? LastFetchError { get; init; }
    public bool AutoSyncEnabled { get; init; }
    public int FetchIntervalMinutes { get; init; }
    public bool WriteEnabled { get; init; }
    public string? DefaultBranch { get; init; }
    public string? CloneUrl { get; init; }
}
