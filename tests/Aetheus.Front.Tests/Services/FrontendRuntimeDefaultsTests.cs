// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Services;

public sealed class FrontendRuntimeDefaultsTests
{
    [Fact]
    public void ApiTimeouts_ArePositiveAndOrdered()
    {
        Assert.True(FrontendRuntimeDefaults.ApiAttemptTimeout > TimeSpan.Zero);
        Assert.True(FrontendRuntimeDefaults.ApiTotalRequestTimeout >= FrontendRuntimeDefaults.ApiAttemptTimeout);
        Assert.True(FrontendRuntimeDefaults.ApiCircuitSamplingDuration >= FrontendRuntimeDefaults.ApiAttemptTimeout);
    }

    [Fact]
    public void SignalRRetrySchedule_IsNonDecreasing()
    {
        Assert.NotEmpty(FrontendRuntimeDefaults.SignalRRetryDelays);
        Assert.Equal(TimeSpan.Zero, FrontendRuntimeDefaults.SignalRRetryDelays[0]);
        Assert.True(FrontendRuntimeDefaults.SignalRRetryDelays
            .Zip(FrontendRuntimeDefaults.SignalRRetryDelays.Skip(1))
            .All(pair => pair.First <= pair.Second));
        Assert.True(
            FrontendRuntimeDefaults.SignalRRetryDelays.TakeWhile(delay => delay <= TimeSpan.FromSeconds(5)).Count() >= 4,
            "Initial SignalR recovery needs several bounded attempts before the long backoff begins.");
    }
}
