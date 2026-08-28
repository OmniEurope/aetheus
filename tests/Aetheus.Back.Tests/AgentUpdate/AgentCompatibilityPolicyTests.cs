// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.AgentUpdate;
using Aetheus.Back.Components.Servers;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests;

public class AgentCompatibilityPolicyTests
{
    private readonly AgentCompatibilityPolicy _sut = new(new TestReleaseCatalog());

    [Fact]
    public void Evaluate_NoHeartbeat_IsUnknown()
    {
        var result = _sut.Evaluate(new ServerDto());

        Assert.Equal(AgentCompatibilityStatus.Unknown, result.Status);
        Assert.Equal(AgentCompatibilityReason.NoHeartbeat, result.Reason);
    }

    [Fact]
    public void Evaluate_CurrentContractAndCapabilities_IsUpToDate()
    {
        var result = _sut.Evaluate(CurrentServer());

        Assert.Equal(AgentCompatibilityStatus.UpToDate, result.Status);
        Assert.Empty(result.MissingCapabilities);
    }

    [Fact]
    public void Evaluate_SupportedOlderSemVer_IsRecommended()
    {
        var server = CurrentServer() with { AgentVersion = "0.9.0" };

        var result = _sut.Evaluate(server);

        Assert.Equal(AgentCompatibilityStatus.UpdateRecommended, result.Status);
        Assert.Equal(AgentCompatibilityReason.OlderSoftware, result.Reason);
    }

    [Fact]
    public void Evaluate_UnsupportedProtocol_IsRequired()
    {
        var server = CurrentServer() with { AgentProtocolVersion = 0 };

        var result = _sut.Evaluate(server);

        Assert.Equal(AgentCompatibilityStatus.UpdateRequired, result.Status);
        Assert.Equal(AgentCompatibilityReason.UnsupportedProtocol, result.Reason);
    }

    [Fact]
    public void Evaluate_PreviousProtocol_IsRequired()
    {
        var server = CurrentServer() with { AgentProtocolVersion = AgentProtocol.CurrentVersion - 1 };

        var result = _sut.Evaluate(server);

        Assert.Equal(AgentCompatibilityStatus.UpdateRequired, result.Status);
        Assert.Equal(AgentCompatibilityReason.UnsupportedProtocol, result.Reason);
    }

    [Fact]
    public void Evaluate_MissingRequiredCapability_IsRequired()
    {
        var server = CurrentServer() with
        {
            PipelineRunnerEnabled = true,
            AgentCapabilities = [AgentCapabilities.SelfUpdate]
        };

        var result = _sut.Evaluate(server);

        Assert.Equal(AgentCompatibilityStatus.UpdateRequired, result.Status);
        Assert.Contains(AgentCapabilities.PipelineBuild, result.MissingCapabilities);
    }

    [Fact]
    public void CurrentProtocolFixture_DeserializesAdditively()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "agent-protocol", "v2-heartbeat.json");
        var heartbeat = JsonSerializer.Deserialize<ServerHeartbeatDto>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(heartbeat);
        Assert.Equal(AgentProtocol.CurrentVersion, heartbeat.AgentProtocolVersion);
        Assert.Contains(AgentCapabilities.SelfUpdate, heartbeat.AgentCapabilities!);
    }

    [Fact]
    public void AgentUpdateWaitingResponse_DeserializesWithFrozenNMinusOneNonNullableTaskId()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "agent-protocol",
            "agent-update-waiting-v2.json");

        var response = JsonSerializer.Deserialize<NMinusOneAgentUpdateResponse>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal(42, response.RequestId);
        Assert.Equal(0, response.TaskId);
        Assert.Equal("WaitingForIdle", response.Status);
    }

    private static ServerDto CurrentServer() => new()
    {
        AgentVersion = "1.0.0",
        AgentProtocolVersion = AgentProtocol.CurrentVersion,
        AgentCapabilities = [AgentCapabilities.SelfUpdate],
        LastHeartbeat = new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc),
        Status = ServerStatus.Online
    };

    private sealed class TestReleaseCatalog : IAgentReleaseCatalog
    {
        public AgentReleaseManifestDto Current { get; } = new()
        {
            SoftwareVersion = "1.0.0",
            ProtocolVersion = AgentProtocol.CurrentVersion,
            MinimumSupportedProtocol = AgentProtocol.MinimumSupportedVersion,
            MaximumSupportedProtocol = AgentProtocol.MaximumSupportedVersion,
            SoftwareCapabilities = [.. AgentCapabilities.SoftwareCapabilities],
            Commit = "test"
        };
    }

    private sealed record NMinusOneAgentUpdateResponse
    {
        public int RequestId { get; init; }
        public int TaskId { get; init; }
        public string Status { get; init; } = string.Empty;
        public string TargetVersion { get; init; } = string.Empty;
        public int BlockingTaskCount { get; init; }
        public bool Created { get; init; }
    }
}
