// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Tests.Servers;

/// <summary>
/// S-TECH-CDUI: the persisted capability-diagnostics JSON is surfaced on the server DTO so the UI can show
/// WHY a capability is off; a null or malformed blob degrades to an empty list, never breaking the DTO.
/// </summary>
public sealed class ServerCapabilityDiagnosticsMappingTests
{
    [Fact]
    public void MapToDto_SurfacesPersistedDiagnostics()
    {
        var server = new Server
        {
            Id = 1,
            Name = "vps",
            LastHeartbeat = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
            CapabilityDiagnosticsJson = "[\"aetheus-apache drop-in present but unreadable\",\"sudo -n probe failed\"]",
        };

        var dto = ServerDataMapper.MapToDto(server);

        Assert.Equal(2, dto.CapabilityDiagnostics.Count);
        Assert.Contains("sudo -n probe failed", dto.CapabilityDiagnostics);
    }

    [Fact]
    public void MapToDto_EmptyDiagnostics_WhenNull()
    {
        var dto = ServerDataMapper.MapToDto(new Server { Id = 1, Name = "vps", CapabilityDiagnosticsJson = null });

        Assert.Empty(dto.CapabilityDiagnostics);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"unexpected\":true}")]
    public void DeserializeDiagnostics_Malformed_ReturnsEmpty(string json)
    {
        Assert.Empty(ServerDataMapper.DeserializeDiagnostics(json));
    }
}
