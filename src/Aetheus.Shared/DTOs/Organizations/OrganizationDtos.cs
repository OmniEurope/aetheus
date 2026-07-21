// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.DTOs.Organizations;

public record OrganizationDto(
    int Id,
    string Name,
    string Slug,
    string Description,
    int MemberCount,
    int ProjectCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record OrganizationDetailDto(
    int Id,
    string Name,
    string Slug,
    string Description,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<OrganizationMemberDto> Members,
    List<OrganizationProjectDto> Projects);

public record OrganizationMemberDto(
    int Id,
    int UserId,
    string Username,
    string? Email,
    OrganizationRole Role,
    DateTime CreatedAt);

public record OrganizationProjectDto(
    int Id,
    string Name,
    ProjectStatus Status);

/// <summary>
/// Lightweight projection of an organization the current user is a member of, used by the
/// header organization picker. Includes the user's role within that organization so the UI
/// can render a badge or restrict actions.
/// </summary>
public record MyOrganizationDto(
    int Id,
    string Name,
    string Slug,
    OrganizationRole Role);

/// <summary>
/// An organization a specific user (by id) is a member of, for the user detail "Organizations"
/// tab. Carries <see cref="MemberId"/> so the page can call the existing member endpoints
/// (<c>PUT/DELETE api/organizations/{orgId}/members/{memberId}</c>) to change the role or remove.
/// </summary>
public record UserOrganizationDto(
    int OrganizationId,
    int MemberId,
    string Name,
    string Slug,
    OrganizationRole Role);

public class CreateOrganizationRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 2)]
    [RegularExpression("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$",
        ErrorMessage = "Slug must be lowercase alphanumeric with optional hyphens.")]
    public string Slug { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;
}

public class UpdateOrganizationRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 2)]
    [RegularExpression("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$",
        ErrorMessage = "Slug must be lowercase alphanumeric with optional hyphens.")]
    public string Slug { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;
}

public class AddOrganizationMemberRequest
{
    [Required]
    public int UserId { get; set; }

    [Required]
    public OrganizationRole Role { get; set; } = OrganizationRole.Member;
}

public class UpdateOrganizationMemberRequest
{
    [Required]
    public OrganizationRole Role { get; set; }
}

public class AssignProjectsRequest
{
    [Required]
    [MaxLength(1000)]
    public List<int> ProjectIds { get; set; } = [];
}
