// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Security.Claims;
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Components.PersonalAccessTokens;
using Aetheus.Back.Configuration;
using Aetheus.Back.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aetheus.Back.Tests.Extensions;

public sealed class RateLimitingExtensionsTests
{
    [Fact]
    public void ResolveGlobalLimit_PartitionsAgentByServerWithDedicatedBound()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("ServerId", "4")],
                AgentTokenAuthenticationHandler.SchemeName))
        };

        var result = RateLimitingExtensions.ResolveGlobalLimit(context);

        Assert.Equal("agent:4", result.PartitionKey);
        Assert.Equal(BackendRuntimeDefaults.AuthenticatedAgentRateLimit, result.PermitLimit);
    }

    [Fact]
    public void ResolveGlobalLimit_KeepsHumanUsersOnTheirOwnLowerPartition()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "admin")],
                "Bearer"))
        };

        var result = RateLimitingExtensions.ResolveGlobalLimit(context);

        Assert.Equal("u:admin", result.PartitionKey);
        Assert.Equal(BackendRuntimeDefaults.AuthenticatedUserRateLimit, result.PermitLimit);
    }

    [Fact]
    public void BearerSelector_AgentPolicyEndpoint_ForwardsBeforeGlobalRateLimit()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer opaque-agent-token";
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AuthorizeAttribute
            {
                Policy = AgentTokenAuthenticationHandler.SchemeName
            }),
            "agent endpoint"));

        var scheme = BearerAuthenticationSchemeSelector.Resolve(context);

        Assert.Equal(AgentTokenAuthenticationHandler.SchemeName, scheme);
    }

    [Fact]
    public void BearerSelector_RegularEndpoint_DoesNotTreatOpaqueTokenAsAgent()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer opaque-token";
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            EndpointMetadataCollection.Empty,
            "regular endpoint"));

        var scheme = BearerAuthenticationSchemeSelector.Resolve(context);

        Assert.Null(scheme);
    }

    [Fact]
    public void BearerSelector_PersonalAccessToken_PreservesPatForwarding()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {PatConstants.TokenPrefix}opaque-token";

        var scheme = BearerAuthenticationSchemeSelector.Resolve(context);

        Assert.Equal(PatConstants.SchemeName, scheme);
    }

    [Fact]
    public void ResolveOtlpPartitionKey_CannotBeRotatedWithUntrustedIngestHeader()
    {
        var first = new DefaultHttpContext();
        first.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        first.Request.Headers["x-aetheus-ingest-key"] = "first-attacker-value";
        var second = new DefaultHttpContext();
        second.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        second.Request.Headers["x-aetheus-ingest-key"] = "second-attacker-value";

        var firstKey = RateLimitingExtensions.ResolveOtlpPartitionKey(first);
        var secondKey = RateLimitingExtensions.ResolveOtlpPartitionKey(second);

        Assert.Equal("otlp-address:192.0.2.10", firstKey);
        Assert.Equal(firstKey, secondKey);
        Assert.DoesNotContain("attacker", firstKey, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveOtlpPartitionKey_IsolatesDifferentRemoteAddresses()
    {
        var first = new DefaultHttpContext();
        first.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        var second = new DefaultHttpContext();
        second.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.11");

        Assert.NotEqual(
            RateLimitingExtensions.ResolveOtlpPartitionKey(first),
            RateLimitingExtensions.ResolveOtlpPartitionKey(second));
    }

    [Theory]
    [InlineData(nameof(AuthController.Login), "login")]
    [InlineData(nameof(AuthController.RegisterServer), "enrollment")]
    [InlineData(nameof(AuthController.Renew), "auth-token")]
    [InlineData(nameof(AuthController.RefreshToken), "auth-token")]
    public void AuthEndpoints_UseIndependentRateLimitPolicies(string methodName, string expectedPolicy)
    {
        var method = typeof(AuthController).GetMethod(methodName)!;
        var attribute = Assert.Single(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>());

        Assert.Equal(expectedPolicy, attribute.PolicyName);
    }

    [Fact]
    public void Login_UsesValidationAuditFilterForPreActionFailures()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Login))!;
        var filter = Assert.Single(method.GetCustomAttributes(typeof(ServiceFilterAttribute), true)
            .Cast<ServiceFilterAttribute>());

        Assert.Equal(typeof(LoginValidationAuditFilter), filter.ServiceType);
    }
}
