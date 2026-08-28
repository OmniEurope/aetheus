// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Tests.Architecture;

public sealed class AgentProtocolCompatibilityArchitectureTests
{
    [Fact]
    public void CompatibilityWindow_AcceptsOnlyCurrentProtocol()
    {
        Assert.Equal(AgentProtocol.CurrentVersion, AgentProtocol.MinimumSupportedVersion);
        Assert.Equal(AgentProtocol.CurrentVersion, AgentProtocol.MaximumSupportedVersion);
        Assert.True(AgentProtocol.IsSupported(AgentProtocol.CurrentVersion));
        Assert.False(AgentProtocol.IsSupported(AgentProtocol.CurrentVersion - 1));
        Assert.False(AgentProtocol.IsSupported(AgentProtocol.CurrentVersion + 1));
    }

    [Fact]
    public void Heartbeat_KeepsCurrentCompatibilitySignals()
    {
        var heartbeat = typeof(ServerHeartbeatDto);

        Assert.NotNull(heartbeat.GetProperty(nameof(ServerHeartbeatDto.DockerAvailable)));
        Assert.NotNull(heartbeat.GetProperty(nameof(ServerHeartbeatDto.PipelineRunnerAvailable)));
        Assert.NotNull(heartbeat.GetProperty(nameof(ServerHeartbeatDto.SudoersHashes)));
        Assert.Equal(
            typeof(int?),
            heartbeat.GetProperty(nameof(ServerHeartbeatDto.AgentProtocolVersion))!.PropertyType);
        Assert.Equal(
            typeof(List<string>),
            Nullable.GetUnderlyingType(
                heartbeat.GetProperty(nameof(ServerHeartbeatDto.AgentCapabilities))!.PropertyType)
            ?? heartbeat.GetProperty(nameof(ServerHeartbeatDto.AgentCapabilities))!.PropertyType);
    }

    [Fact]
    public void EveryOperationCapability_IsAStableCatalogValue()
    {
        var catalog = typeof(AgentCapabilities)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var operation in Enum.GetValues<Aetheus.Shared.Enums.OperationKind>())
        {
            var required = AgentCapabilities.RequiredFor(operation);
            Assert.True(required is not null, $"{operation} has no required agent capability.");
            Assert.Contains(required, catalog);
        }
    }

    [Fact]
    public void AiRun_RequiresTheCapabilityPublishedByCurrentAgents()
    {
        Assert.Equal(
            AgentCapabilities.AiExecution,
            AgentCapabilities.RequiredFor(Aetheus.Shared.Enums.OperationKind.AiRun));
        Assert.Contains(AgentCapabilities.AiExecution, AgentCapabilities.SoftwareCapabilities);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
