// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data;

/// <summary>
/// Names of the EF query filters that hide retired servers (PLAN-004 R-11). One name covers the
/// <see cref="Entities.Server"/> filter and the filters of the two link tables an admin rewrites in
/// bulk (<see cref="Entities.EnvironmentServer"/>, <see cref="Entities.AgentPoolServer"/>), so
/// <c>IgnoreQueryFilters([ServerQueryFilters.ExcludeRetired])</c> lifts the whole retirement view at
/// once. Only the paths that must see retired rows use it: enrollment matching (revival), the
/// retired-server list and purge, the task watchdog, and historic run details.
/// </summary>
public static class ServerQueryFilters
{
    public const string ExcludeRetired = "ExcludeRetiredServers";
}
