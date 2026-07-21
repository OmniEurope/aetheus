// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;

namespace Aetheus.Agent.Core.Tests;

public sealed class AgentRuntimeDefaultsTests
{
    [Fact]
    public void Defaults_ArePositiveAndOrdered()
    {
        Assert.True(AgentRuntimeDefaults.CapabilityProbeTimeout > TimeSpan.Zero);
        Assert.True(AgentRuntimeDefaults.ShellCommandTimeout >= AgentRuntimeDefaults.CapabilityProbeTimeout);
        Assert.True(AgentRuntimeDefaults.SlowInventoryTtl > AgentRuntimeDefaults.DockerInventoryTtl);
        Assert.True(AgentRuntimeDefaults.MaximumTaskTimeoutSeconds >= AgentRuntimeDefaults.MinimumTaskTimeoutSeconds);
    }

    [Fact]
    public void Options_UseCentralRuntimeDefaults()
    {
        var options = new AetheusAgentOptions();

        Assert.Equal(AgentRuntimeDefaults.PollingIntervalSeconds, options.PollingIntervalSeconds);
        Assert.Equal(AgentRuntimeDefaults.HeartbeatIntervalSeconds, options.HeartbeatIntervalSeconds);
        Assert.Equal(AgentRuntimeDefaults.HeartbeatCollectionTimeoutSeconds, options.HeartbeatCollectionTimeoutSeconds);
        Assert.Equal(AgentRuntimeDefaults.MaximumConcurrentTasks, options.MaxConcurrentTasks);
    }
}
