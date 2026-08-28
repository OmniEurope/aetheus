// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Tasks;

namespace Aetheus.Back.Tests;

public sealed class AgentTaskProtocolCompatibilityTests
{
    [Theory]
    [InlineData("1.0.395")]
    [InlineData("v1.0.395")]
    [InlineData("1.0.760")]
    public void PreviousAgentWindow_AllowsUnfencedRequests(string version)
    {
        Assert.True(AgentTaskProtocolCompatibility.AllowsLegacyUnfencedRequests(version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dev")]
    [InlineData("1.0.394")]
    [InlineData("1.0.761")]
    [InlineData("2.0.0")]
    public void UnknownOldOrFencedAgent_RequiresFencing(string? version)
    {
        Assert.False(AgentTaskProtocolCompatibility.AllowsLegacyUnfencedRequests(version));
    }
}
