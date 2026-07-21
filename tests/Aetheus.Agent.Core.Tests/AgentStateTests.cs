// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;

namespace Aetheus.Agent.Core.Tests;

public class AgentStateTests
{
    [Fact]
    public void IsEnrolled_ReturnsTrue_WhenServerIdAndTokenSet()
    {
        var state = new AgentState { ServerId = 1, BearerToken = "token" };
        Assert.True(state.IsEnrolled);
    }

    [Fact]
    public void IsEnrolled_ReturnsFalse_WhenNoServerId()
    {
        var state = new AgentState { ServerId = null, BearerToken = "token" };
        Assert.False(state.IsEnrolled);
    }

    [Fact]
    public void IsEnrolled_ReturnsFalse_WhenNoToken()
    {
        var state = new AgentState { ServerId = 1, BearerToken = null };
        Assert.False(state.IsEnrolled);
    }

    [Fact]
    public void IsEnrolled_ReturnsFalse_WhenEmptyToken()
    {
        var state = new AgentState { ServerId = 1, BearerToken = "" };
        Assert.False(state.IsEnrolled);
    }

    [Fact]
    public void IsTokenExpired_ReturnsTrue_WhenPastExpiry()
    {
        var state = new AgentState { TokenExpiresAt = DateTime.UtcNow.AddMinutes(-1) };
        Assert.True(state.IsTokenExpired);
    }

    [Fact]
    public void IsTokenExpired_ReturnsFalse_WhenBeforeExpiry()
    {
        var state = new AgentState { TokenExpiresAt = DateTime.UtcNow.AddHours(1) };
        Assert.False(state.IsTokenExpired);
    }

    [Fact]
    public void IsTokenExpired_ReturnsFalse_WhenNoExpiry()
    {
        var state = new AgentState { TokenExpiresAt = null };
        Assert.False(state.IsTokenExpired);
    }
}
