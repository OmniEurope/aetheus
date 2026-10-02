// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers.ServerDetailSections;
namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerOverviewSectionMethodTests
{
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;

    // === FormatAgentVersion (private static) ===

    [Theory]
    [InlineData(null, "-")]
    [InlineData("", "-")]
    [InlineData("   ", "-")]
    [InlineData("1.2.3.4", "v1.2.3")]
    [InlineData("1.2.3.4.5", "v1.2.3")]
    [InlineData("1.2.3", "v1.2.3")]
    [InlineData("1.2", "v1.2")]
    [InlineData("1", "v1")]
    [InlineData("2.5.0.12345", "v2.5.0")]
    public void FormatAgentVersion_ReturnsExpected(string? version, string expected)
    {
        var method = typeof(ServerOverviewSection).GetMethod("FormatAgentVersion", PrivStatic)!;
        var result = (string)method.Invoke(null, [version])!;
        Assert.Equal(expected, result);
    }
}
