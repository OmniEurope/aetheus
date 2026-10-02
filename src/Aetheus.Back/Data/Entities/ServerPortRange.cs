// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// The window of host ports Aetheus may hand out on one server (PLAN-005 lot 4). One row per server,
/// absent for most of them: the default window is the one the fleet already uses in practice, so a
/// server only needs a row when it deviates.
///
/// It exists because allocation must not be free to pick anything: a port below 1024 needs
/// privileges, and the QA range starts at 20000, so an unbounded search would eventually hand out a
/// port that cannot be bound or that belongs to another convention.
/// </summary>
public class ServerPortRange
{
    public int Id { get; set; }
    public int ServerId { get; set; }

    /// <summary>First port of the window, inclusive.</summary>
    public int From { get; set; }

    /// <summary>Last port of the window, inclusive.</summary>
    public int To { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
