// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;
using Microsoft.Extensions.Configuration;

namespace Aetheus.Back.Tests.Configuration;

public sealed class PublicDemoConfigurationTests
{
    [Fact]
    public void IsEnabled_AllExplicitDemoMarkersPresent_ReturnsTrue()
    {
        var configuration = BuildConfiguration(publicDemo: "true", seedDemo: "true", tier: "qa");

        Assert.True(PublicDemoConfiguration.IsEnabled(configuration));
    }

    [Theory]
    [InlineData(null, "true", "qa")]
    [InlineData("false", "true", "qa")]
    [InlineData("true", null, "qa")]
    [InlineData("true", "false", "qa")]
    [InlineData("true", "true", "production")]
    public void IsEnabled_MissingOrInvalidMarker_ReturnsFalse(
        string? publicDemo,
        string? seedDemo,
        string? tier)
    {
        var configuration = BuildConfiguration(publicDemo, seedDemo, tier);

        Assert.False(PublicDemoConfiguration.IsEnabled(configuration));
    }

    private static IConfiguration BuildConfiguration(string? publicDemo, string? seedDemo, string? tier)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aetheus:PublicDemo"] = publicDemo,
                ["Seed:Demo"] = seedDemo,
                ["Aetheus:EnvironmentTier"] = tier
            })
            .Build();
}
