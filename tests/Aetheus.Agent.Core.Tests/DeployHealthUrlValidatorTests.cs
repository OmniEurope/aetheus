// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Tests;

public sealed class DeployHealthUrlValidatorTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8080/health/ready")]
    [InlineData("https://localhost/ready")]
    [InlineData("http://[::1]:8080/ready")]
    public void LoopbackHttpUrls_AreAccepted(string value)
    {
        Assert.True(DeployHealthUrlValidator.TryNormalize(value, out var normalized));
        Assert.False(string.IsNullOrWhiteSpace(normalized));
    }

    [Theory]
    [InlineData("http://10.0.0.1/ready")]
    [InlineData("https://example.com/ready")]
    [InlineData("http://user:password@127.0.0.1/ready")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://127.0.0.1/ready#fragment")]
    public void NonLoopbackOrAmbiguousUrls_AreRejected(string value)
    {
        Assert.False(DeployHealthUrlValidator.TryNormalize(value, out _));
    }
}
