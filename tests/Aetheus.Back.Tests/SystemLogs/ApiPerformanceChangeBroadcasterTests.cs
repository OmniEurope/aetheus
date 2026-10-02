// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.SystemLogs;
using Aetheus.Back.Services;
using Aetheus.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.SystemLogs;

/// <summary>R-181: the push that replaces the Refresh button of the administration Performance page.</summary>
public sealed class ApiPerformanceChangeBroadcasterTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly IAdminChangeNotifier _notifier = Substitute.For<IAdminChangeNotifier>();
    private readonly RequestPerformanceRecorder _recorder;
    private readonly ApiPerformanceChangeBroadcaster _sut;

    public ApiPerformanceChangeBroadcasterTests()
    {
        _recorder = new RequestPerformanceRecorder(Options.Create(AetheusOptions()), _time);
        _sut = new ApiPerformanceChangeBroadcaster(
            _recorder, _notifier, _time, NullLogger<ApiPerformanceChangeBroadcaster>.Instance);
    }

    /// <summary>The recorder as Aetheus configures it (API routes only, the report route quiet).</summary>
    private static RequestPerformanceOptions AetheusOptions()
    {
        var options = new RequestPerformanceOptions();
        ApiPerformanceReport.Configure(options);
        return options;
    }

    public void Dispose()
    {
        _sut.Dispose();
        _recorder.Dispose();
    }

    [Fact]
    public async Task ARecordedSample_IsAnnouncedOnce_ToTheAdminHub()
    {
        _recorder.Record("api/projects", "GET", 200, 12);
        _recorder.Record("api/pipelines/{id}", "GET", 200, 30);

        await _sut.AnnounceNextChangeAsync(TestContext.Current.CancellationToken);

        await _notifier.Received(1).BroadcastAsync(
            AdminEntities.ApiPerformance, 0, EntityChangeOps.Updated, Arg.Any<CancellationToken>());
        // Both samples collapsed into that single announcement: nothing is pending any more.
        Assert.False(_recorder.WaitForChangeAsync(TestContext.Current.CancellationToken).IsCompleted);
    }

    [Fact]
    public void NoTraffic_AnnouncesNothing()
    {
        var announce = _sut.AnnounceNextChangeAsync(TestContext.Current.CancellationToken);

        Assert.False(announce.IsCompleted);
        _notifier.DidNotReceive().BroadcastAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ThePagesOwnReport_DoesNotAnnounceAChange_OrAnOpenPageWouldReloadItselfForever()
    {
        _recorder.Record(ApiPerformanceReport.ReportRoute, "GET", 200, 5);

        Assert.False(_recorder.WaitForChangeAsync(TestContext.Current.CancellationToken).IsCompleted);
        // The sample itself is still measured.
        Assert.Single(_recorder.Snapshot());
    }

    [Fact]
    public void StaticFilesAndProbes_AreNeitherMeasuredNorAnnounced()
    {
        _recorder.Record("health", "GET", 200, 1);

        Assert.False(_recorder.WaitForChangeAsync(TestContext.Current.CancellationToken).IsCompleted);
        Assert.Empty(_recorder.Snapshot());
    }

    [Fact]
    public async Task AFailedPush_DoesNotStopTheAnnouncements()
    {
        _notifier.BroadcastAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("hub down")), Task.CompletedTask);

        _recorder.Record("api/projects", "GET", 200, 12);
        await _sut.AnnounceNextChangeAsync(TestContext.Current.CancellationToken);
        _recorder.Record("api/projects", "GET", 200, 14);
        await _sut.AnnounceNextChangeAsync(TestContext.Current.CancellationToken);

        await _notifier.Received(2).BroadcastAsync(
            AdminEntities.ApiPerformance, 0, EntityChangeOps.Updated, Arg.Any<CancellationToken>());
    }
}
