// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Drift guard for the "capability flag added to the entity + DTO but forgotten in the mapper" bug
/// class - it has already shipped twice (<c>DeploymentTargetAvailable</c> read by the UI but never
/// mapped → dead "Déploiement" badge; <c>MailSetupAvailable</c> nearly the same). The UI gates real
/// actions on these <c>*Available</c> flags, so a flag stuck <c>false</c> by an omission in
/// <see cref="ServerDataMapper.MapToDto"/> silently disables a feature with no compile error.
///
/// This test reflects over every <c>bool *Available</c> property on <see cref="ServerDto"/>, sets the
/// matching <see cref="Server"/> entity property to <c>true</c>, maps, and asserts the DTO carries it
/// through. A newly-added capability flag that the mapper forgets fails the build here.
/// </summary>
public sealed class ServerCapabilityMappingAuditTests
{
    [Fact]
    public void Every_capability_flag_is_mapped_from_entity_to_dto()
    {
        // Match both bool and bool? "*Available" flags (S-TECH-CUNK made some tri-state on the tile).
        var dtoFlags = typeof(ServerDto).GetProperties()
            .Where(p => (p.PropertyType == typeof(bool) || p.PropertyType == typeof(bool?))
                        && p.Name.EndsWith("Available", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(dtoFlags); // sanity: the reflection target must not silently match nothing.

        // A tri-state flag maps to null until the agent has phoned home once, so give the guard server a
        // heartbeat - otherwise MapToDto would legitimately return null for the CUNK flags.
        var server = new Server { Name = "guard", Hostname = "guard", LastHeartbeat = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        foreach (var dtoFlag in dtoFlags)
        {
            var entityProp = typeof(Server).GetProperty(dtoFlag.Name, BindingFlags.Public | BindingFlags.Instance);
            Assert.True(
                entityProp is { PropertyType: var t } && t == typeof(bool),
                $"ServerDto.{dtoFlag.Name} has no matching bool '{dtoFlag.Name}' on the Server entity - "
                + "the mapper cannot carry a flag the entity does not hold.");
            entityProp!.SetValue(server, true);
        }

        var dto = ServerDataMapper.MapToDto(server);

        foreach (var dtoFlag in dtoFlags)
        {
            Assert.True(
                (bool?)dtoFlag.GetValue(dto) == true,
                $"ServerDto.{dtoFlag.Name} was true on the entity but not true after MapToDto - "
                + "the capability flag is not wired in ServerDataMapper.MapToDto.");
        }
    }
}
