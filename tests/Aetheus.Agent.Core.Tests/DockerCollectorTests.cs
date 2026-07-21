// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Collectors;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class DockerCollectorTests
{
    [Theory]
    [InlineData("45.5%", 45.5)]
    [InlineData("0.00%", 0.0)]
    [InlineData("100%", 100.0)]
    [InlineData("  12.3% ", 12.3)]
    [InlineData("invalid", 0.0)]
    [InlineData("", 0.0)]
    public void ParsePercent_ReturnsExpected(string input, double expected)
    {
        Assert.Equal(expected, DockerCollector.ParsePercent(input));
    }

    [Theory]
    [InlineData("256MiB / 2GiB", 256.0, 2048.0)]
    [InlineData("1.5GiB / 4GiB", 1536.0, 4096.0)]
    [InlineData("512KB / 1MB", 0.512, 1.0)]
    [InlineData("100MB / 200MB", 100.0, 200.0)]
    [InlineData("invalid", 0.0, 0.0)]
    public void ParseMemUsage_ReturnsExpected(string input, double expectedUsed, double expectedLimit)
    {
        var result = DockerCollector.ParseMemUsage(input);
        Assert.Equal(expectedUsed, result.UsedMb, 1);
        Assert.Equal(expectedLimit, result.LimitMb, 1);
    }

    [Theory]
    [InlineData("1GiB", 1024.0)]
    [InlineData("2GB", 2000.0)]
    [InlineData("512MiB", 512.0)]
    [InlineData("256MB", 256.0)]
    [InlineData("1024KiB", 1.0)]
    [InlineData("1024KB", 1.024)]
    [InlineData("1TiB", 1048576.0)]
    [InlineData("1TB", 1000000.0)]
    [InlineData("1000000B", 1.0)]
    [InlineData("invalid", 0.0)]
    public void ParseSizeToMb_ReturnsExpected(string input, double expected)
    {
        Assert.Equal(expected, DockerCollector.ParseSizeToMb(input), 1);
    }

    [Fact]
    public void ParseDockerDate_ValidDate_ReturnsUtc()
    {
        var result = DockerCollector.ParseDockerDate("2026-01-15 10:30:00 +0000 UTC");
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void ParseDockerDate_InvalidDate_ReturnsMinValue()
    {
        // audit: parse failure now yields the MinValue sentinel instead of a fake "now".
        var result = DockerCollector.ParseDockerDate("not-a-date");
        Assert.Equal(DateTime.MinValue, result);
    }

    [Theory]
    [InlineData("myapp_service_1", "myapp")]
    [InlineData("myapp-service-1", "myapp")]
    [InlineData("justname", "")]
    [InlineData("", "")]
    [InlineData("_prefix", "")]
    public void InferProjectFromName_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, DockerCollector.InferProjectFromName(input));
    }
}
