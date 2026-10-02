// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>A user following a project: the recipients of a project's events are its subscribers.</summary>
public class ProjectSubscription
{
    public int UserId { get; set; }
    public int ProjectId { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = default!;
    public Project Project { get; set; } = default!;
}
