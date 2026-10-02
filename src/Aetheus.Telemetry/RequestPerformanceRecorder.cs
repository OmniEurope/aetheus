// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Aetheus.Telemetry;

/// <summary>One timed request, under its route template in lower case: no concrete path, identifier or
/// query value.</summary>
public readonly record struct RequestTimingSample(DateTime At, string Method, string Route, int StatusCode, double DurationMs);

/// <summary>
/// Listens, in process, to the request-duration histogram ASP.NET Core publishes
/// (<c>Microsoft.AspNetCore.Hosting</c> / <c>http.server.request.duration</c>) and keeps each request of
/// the last <see cref="Window"/>, capped at <see cref="MaxSamples"/>. Reading the instrument here rather
/// than from exported data means the figures exist even when the OTLP export is off.
///
/// The window lives in this process's memory and restarts with it; <see cref="RequestPerformanceReport"/>
/// states that through <see cref="RequestPerformanceSummary.Since"/>.
/// </summary>
public sealed class RequestPerformanceRecorder : IHostedService, IDisposable
{
    public const string MeterName = "Microsoft.AspNetCore.Hosting";
    public const string InstrumentName = "http.server.request.duration";
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>About 100 bytes each: a few megabytes at most, whatever the traffic.</summary>
    public const int MaxSamples = 50_000;

    private readonly ConcurrentQueue<RequestTimingSample> _samples = new();
    private readonly SemaphoreSlim _changed = new(0, 1);
    private readonly TimeProvider _time;
    private readonly RequestPerformanceOptions _options;
    private MeterListener? _listener;
    private int _count;
    private int _truncated;

    public RequestPerformanceRecorder(IOptions<RequestPerformanceOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        _options = options.Value;
        _time = time;
    }

    /// <summary>True once the cap was reached and the oldest samples of the window were dropped.</summary>
    public bool Truncated => Volatile.Read(ref _truncated) == 1;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MeterName && instrument.Name == InstrumentName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>(OnMeasurement);
        _listener.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Dispose();
        _listener = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _changed.Dispose();
    }

    /// <summary>
    /// Completes once a sample has been recorded since the previous wait returned (samples of the
    /// <see cref="RequestPerformanceOptions.QuietRoutes"/> excepted). Any number of samples in between
    /// collapse into one signal.
    /// </summary>
    public Task WaitForChangeAsync(CancellationToken ct) => _changed.WaitAsync(ct);

    private void OnMeasurement(Instrument instrument, double seconds, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        string? route = null;
        var method = string.Empty;
        var status = 0;
        foreach (var tag in tags)
        {
            switch (tag.Key)
            {
                case "http.route": route = tag.Value as string; break;
                case "http.request.method": method = tag.Value as string ?? string.Empty; break;
                case "http.response.status_code": status = tag.Value is int code ? code : 0; break;
            }
        }

        Record(route, method, status, seconds * 1000);
    }

    /// <summary>Adds one request; the entry point of the listener, public so tests can feed it.</summary>
    public void Record(string? route, string method, int statusCode, double durationMs)
    {
        // No template means no endpoint matched (a 404, a static file): nothing to group it under.
        if (route is null || !IsMeasured(route)) return;

        // Recette R-476: ASP.NET Core writes a template in the case of its source (api/Orders from a
        // controller class, api/orders from an attribute) and routes both alike: one family, one line.
        _samples.Enqueue(new RequestTimingSample(
            _time.GetUtcNow().UtcDateTime, method, route.ToLowerInvariant(), statusCode, durationMs));
        if (Interlocked.Increment(ref _count) > MaxSamples && _samples.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
            Volatile.Write(ref _truncated, 1);
        }

        if (!_options.QuietRoutes.Contains(route))
            SignalChange();
    }

    private bool IsMeasured(string route) => _options.RouteFilter is { } filter
        ? filter(route)
        : !TelemetryRoutePolicy.IsExcludedPath("/" + route.TrimStart('/'));

    private void SignalChange()
    {
        // A binary signal: already raised means a waiter will see this sample in the same report.
        if (_changed.CurrentCount > 0) return;
        try { _changed.Release(); }
        catch (SemaphoreFullException) { } // two requests raced to raise the same signal
    }

    /// <summary>The samples of the last <see cref="Window"/>, dropping older ones as it goes.</summary>
    public IReadOnlyList<RequestTimingSample> Snapshot()
    {
        var cutoff = _time.GetUtcNow().UtcDateTime - Window;
        while (_samples.TryPeek(out var oldest) && oldest.At < cutoff && _samples.TryDequeue(out _))
            Interlocked.Decrement(ref _count);
        return [.. _samples];
    }
}
