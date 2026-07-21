// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// Many-to-many link between an <see cref="Environment"/> and a (purely informational)
/// <see cref="ProjectServer"/>. Distinct from <see cref="EnvironmentServer"/>, which links
/// environments to fleet <see cref="Server"/>s. When linked, each side gains read access to the
/// other's libraries / vaults / pipelines (cross-access resolution).
/// </summary>
public class EnvironmentProjectServer
{
    public int EnvironmentId { get; set; }
    public int ProjectServerId { get; set; }

    // Navigation
    public Environment Environment { get; set; } = null!;
    public ProjectServer ProjectServer { get; set; } = null!;
}
