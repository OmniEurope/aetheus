// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Reflection;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Shared;

public sealed class AppIngestionSettingsBehaviorTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public AppIngestionSettingsBehaviorTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void ReadOnlyApp_LoadsAndRendersThresholdsWithoutMutationControls()
    {
        _handler.SetJsonResponse("api/appmonitoring/apps/7/thresholds", new List<AppMetricThresholdDto>
        {
            new() { Id = 3, MonitoredAppId = 7, MetricName = "cpu", Operator = ComparisonOperator.GreaterThan, Threshold = 90, IsBreached = true }
        });

        var cut = Render<AppIngestionSettings>(p => p
            .Add(x => x.AppId, 7)
            .Add(x => x.App, new MonitoredAppDto { Id = 7, Name = "API", HasIngestKey = true, IngestDroppedCount = 2 })
            .Add(x => x.CanWrite, false));

        Assert.Contains("cpu", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Breached", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("IngestDroppedCount", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateKey", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateKey_ShowsPlaintextOnce_NotifiesAndRaisesChanged()
    {
        _handler.SetJsonResponse("api/appmonitoring/apps/7/thresholds", new List<AppMetricThresholdDto>());
        _handler.SetJsonResponse(HttpMethod.Post, "api/appmonitoring/apps/7/ingest-key",
            new IngestKeyResponse { Key = "aetheus-secret-once", CreatedAt = DateTime.UtcNow });
        var changes = 0;
        var cut = Render<AppIngestionSettings>(p => p
            .Add(x => x.AppId, 7)
            .Add(x => x.CanWrite, true)
            .Add(x => x.OnChanged, () => changes++));

        await InvokeAsync(cut, "GenerateKeyAsync");
        cut.Render();

        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.EndsWith("api/appmonitoring/apps/7/ingest-key", StringComparison.Ordinal));
        Assert.Contains("aetheus-secret-once", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("OTEL_EXPORTER_OTLP_HEADERS", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(1, changes);
        Assert.Contains(Services.GetRequiredService<NotificationService>().Messages,
            message => message.Severity == NotificationSeverity.Success);
    }

    [Fact]
    public async Task GenerateKey_ApiFailure_ShowsErrorAndDoesNotRaiseChanged()
    {
        _handler.SetJsonResponse("api/appmonitoring/apps/7/thresholds", new List<AppMetricThresholdDto>());
        _handler.SetResponse(HttpMethod.Post, "api/appmonitoring/apps/7/ingest-key", HttpStatusCode.BadGateway);
        var changes = 0;
        var cut = Render<AppIngestionSettings>(p => p
            .Add(x => x.AppId, 7)
            .Add(x => x.CanWrite, true)
            .Add(x => x.OnChanged, () => changes++));

        await InvokeAsync(cut, "GenerateKeyAsync");

        Assert.Equal(0, changes);
        Assert.Contains(Services.GetRequiredService<NotificationService>().Messages,
            message => message.Severity == NotificationSeverity.Error);
    }

    [Fact]
    public async Task AddThreshold_ValidatesThenPostsAndReloadsGrid()
    {
        _handler.SetJsonResponse("api/appmonitoring/apps/7/thresholds", new List<AppMetricThresholdDto>());
        _handler.SetJsonResponse(HttpMethod.Post, "api/appmonitoring/apps/7/thresholds",
            new AppMetricThresholdDto { Id = 8, MonitoredAppId = 7, MetricName = "memory", Threshold = 2048 });
        var cut = Render<AppIngestionSettings>(p => p.Add(x => x.AppId, 7).Add(x => x.CanWrite, true));

        await InvokeAsync(cut, "AddThresholdAsync");
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST");
        Assert.Contains(Services.GetRequiredService<NotificationService>().Messages,
            message => message.Severity == NotificationSeverity.Warning);

        var request = (CreateMetricThresholdRequest)typeof(AppIngestionSettings)
            .GetField("_newThreshold", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        request.MetricName = "memory";
        request.Threshold = 2048;
        await InvokeAsync(cut, "AddThresholdAsync");

        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.EndsWith("api/appmonitoring/apps/7/thresholds", StringComparison.Ordinal));
        Assert.Equal(string.Empty, request.MetricName);
        Assert.Equal(0, request.Threshold);
    }

    [Fact]
    public async Task DeleteThreshold_UsesThresholdEndpointAndReloads()
    {
        _handler.SetJsonResponse("api/appmonitoring/apps/7/thresholds", new List<AppMetricThresholdDto>
        {
            new() { Id = 8, MonitoredAppId = 7, MetricName = "memory", Threshold = 2048 }
        });
        _handler.SetResponse(HttpMethod.Delete, "api/appmonitoring/thresholds/8", HttpStatusCode.NoContent);
        var cut = Render<AppIngestionSettings>(p => p.Add(x => x.AppId, 7).Add(x => x.CanWrite, true));

        await cut.InvokeAsync(() => (Task)typeof(AppIngestionSettings)
            .GetMethod("DeleteThresholdAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(cut.Instance, [8])!);

        Assert.Contains(_handler.Requests, r => r.Method == "DELETE" && r.Url.EndsWith("api/appmonitoring/thresholds/8", StringComparison.Ordinal));
        Assert.True(_handler.Requests.Count(r => r.Method == "GET" && r.Url.Contains("/thresholds", StringComparison.Ordinal)) >= 2);
    }

    [Fact]
    public void ThresholdLoadFailure_IsContainedAndRendersEmptyState()
    {
        _handler.SetResponse(HttpMethod.Get, "api/appmonitoring/apps/7/thresholds", HttpStatusCode.ServiceUnavailable);

        var cut = Render<AppIngestionSettings>(p => p.Add(x => x.AppId, 7).Add(x => x.CanWrite, true));

        Assert.Contains("NoThresholds", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("AddThreshold", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OlderAppLoad_CannotReplaceCurrentAppThresholds()
    {
        var oldLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldResponse = new TaskCompletionSource<List<AppMetricThresholdDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.SetAsyncJsonResponse(HttpMethod.Get, "api/appmonitoring/apps/7/thresholds", async ct =>
        {
            oldLoadStarted.TrySetResult();
            return await oldResponse.Task.WaitAsync(ct);
        });
        _handler.SetJsonResponse(HttpMethod.Get, "api/appmonitoring/apps/8/thresholds", new List<AppMetricThresholdDto>
        {
            new() { Id = 8, MonitoredAppId = 8, MetricName = "current-app" }
        });
        var cut = Render<AppIngestionSettings>(parameters => parameters.Add(component => component.AppId, 7));
        await oldLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), Xunit.TestContext.Current.CancellationToken);

        cut.Render(parameters => parameters.Add(component => component.AppId, 8));
        cut.WaitForAssertion(() => Assert.Contains("current-app", cut.Markup, StringComparison.Ordinal));

        oldResponse.SetResult([
            new AppMetricThresholdDto { Id = 7, MonitoredAppId = 7, MetricName = "old-app" }
        ]);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("current-app", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("old-app", cut.Markup, StringComparison.Ordinal);
        });
        Assert.Collection(_handler.Requests.Where(request => request.Url.Contains("/thresholds", StringComparison.Ordinal)),
            oldRequest => Assert.EndsWith("api/appmonitoring/apps/7/thresholds", oldRequest.Url, StringComparison.Ordinal),
            currentRequest => Assert.EndsWith("api/appmonitoring/apps/8/thresholds", currentRequest.Url, StringComparison.Ordinal));
    }

    private static Task InvokeAsync(IRenderedComponent<AppIngestionSettings> cut, string method) => cut.InvokeAsync(() =>
        (Task)typeof(AppIngestionSettings).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(cut.Instance, [])!);
}
