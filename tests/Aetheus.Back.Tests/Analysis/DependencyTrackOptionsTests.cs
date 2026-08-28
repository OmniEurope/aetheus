// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;

namespace Aetheus.Back.Tests.Analysis;

public sealed class DependencyTrackOptionsTests
{
    [Fact]
    public void IsValid_AcceptsDocumentedDefaults()
    {
        Assert.True(new DependencyTrackOptions().IsValid());
    }

    [Fact]
    public void IsValid_RejectsRequiredProviderWhenDisabled()
    {
        Assert.False(new DependencyTrackOptions { Required = true }.IsValid());
    }

    [Theory]
    [InlineData(4, 8, 30)]
    [InlineData(15, 0, 30)]
    [InlineData(15, 21, 30)]
    [InlineData(15, 8, 0)]
    [InlineData(301, 8, 30)]
    public void IsValid_RejectsUnsafeOutboxSettings(int pollSeconds, int attempts, int retrySeconds)
    {
        var options = new DependencyTrackOptions
        {
            OutboxPollSeconds = pollSeconds,
            MaxSubmissionAttempts = attempts,
            RetryBaseSeconds = retrySeconds
        };

        Assert.False(options.IsValid());
    }
}
