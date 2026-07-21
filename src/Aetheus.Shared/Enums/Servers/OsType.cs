// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

/// <summary>
/// Structured operating-system family of a server's agent, derived from the free-text
/// <c>OsDescription</c> the agent reports. Used to type pipeline stages/jobs (<c>os:</c>) so a
/// stage can be pinned to a Linux- or Windows-only runner.
/// </summary>
public enum OsType
{
    /// <summary>OS family not yet determined (no constraint when used as a stage requirement).</summary>
    Unknown = 0,
    Linux = 1,
    Windows = 2
}
