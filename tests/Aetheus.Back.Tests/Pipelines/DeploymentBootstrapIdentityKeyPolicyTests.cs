// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Microsoft.Extensions.Configuration;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// A360-59. The deployment bootstrap identity used to fall back on Auth:EncryptionKey whenever no
/// dedicated key was configured. HKDF separates the derivation domains, but it cannot separate the
/// consequences: while the two share a secret, leaking the data-encryption key also leaks an
/// administrator credential for every environment this backend deploys.
///
/// The fallback is refused in production only. Forcing it everywhere would make a developer configure
/// a second secret before running any deployment, which buys nothing.
/// </summary>
public sealed class DeploymentBootstrapIdentityKeyPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

    private static IConfiguration Config(string? environment, string? dedicatedKey, string? encryptionKey)
    {
        var values = new Dictionary<string, string?>();
        if (environment is not null) values["ASPNETCORE_ENVIRONMENT"] = environment;
        if (dedicatedKey is not null) values["Deployment:BootstrapIdentityKey"] = dedicatedKey;
        if (encryptionKey is not null) values["Auth:EncryptionKey"] = encryptionKey;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void InProduction_TheDataKeyFallback_IsRefused()
    {
        var config = Config("Production", dedicatedKey: null, encryptionKey: "the-shared-data-key");

        var exception = Assert.Throws<InvalidOperationException>(
            () => DeploymentBootstrapIdentity.For(config, runId: 42, Now));

        Assert.Contains("Deployment:BootstrapIdentityKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("production", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InProduction_ADedicatedKey_IsAccepted()
    {
        var config = Config("Production", dedicatedKey: "the-dedicated-bootstrap-key", encryptionKey: "data");

        var identity = DeploymentBootstrapIdentity.For(config, runId: 42, Now);

        Assert.False(string.IsNullOrWhiteSpace(identity.Password));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData(null)]
    public void OutsideProduction_TheFallbackStillWorks(string? environment)
    {
        var config = Config(environment, dedicatedKey: null, encryptionKey: "the-shared-data-key");

        var identity = DeploymentBootstrapIdentity.For(config, runId: 42, Now);

        Assert.False(string.IsNullOrWhiteSpace(identity.Password));
    }

    [Fact]
    public void WithNoKeyAtAll_ItStillFailsClosed_Everywhere()
    {
        // The pre-existing guarantee must survive the new one: an empty-keyed HMAC would make the
        // credential predictable to anyone who knows the run id, which on a deployment is public.
        var config = Config("Development", dedicatedKey: null, encryptionKey: null);

        Assert.Throws<InvalidOperationException>(
            () => DeploymentBootstrapIdentity.For(config, runId: 42, Now));
    }
}
