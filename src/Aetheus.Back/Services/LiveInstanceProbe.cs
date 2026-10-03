// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http.Json;

namespace Aetheus.Back.Services;

/// <summary>Whether this backend is the one the public API address reaches.</summary>
public enum InstanceServingState
{
    /// <summary>Not measured, or not measurable (no public URL, probe failure): behave as before.</summary>
    Unknown,

    /// <summary>The public API address answered with this instance's id.</summary>
    Live,

    /// <summary>The public API address answered with another instance's id: this is a reserve colour.</summary>
    Standby
}

public interface ILiveInstanceProbe
{
    /// <summary>A random id drawn at startup, published on <c>/health/instance</c>.</summary>
    string InstanceId { get; }

    InstanceServingState State { get; }
}

/// <summary>
/// Tells a blue-green backend whether it is the colour behind the public API address (decision of
/// 2026-10-02: only the live colour runs leader background work). Commit keeps the replaced colour
/// running in reserve with the previous release's code; a first-come advisory lease let that reserve
/// keep the scheduler, which refused the 02:00 nightly with a check the live release had fixed.
/// <para>
/// The truth is asked of the address itself rather than recorded at switch time: the native switch,
/// its rollback, release-fast and both watchdog reverts all move traffic without telling the backend.
/// Only a positive answer naming another instance makes this one <see cref="InstanceServingState.Standby"/>;
/// no URL, an unreachable URL or an unreadable answer stays <see cref="InstanceServingState.Unknown"/>,
/// which keeps today's behaviour (development, a single instance, a probe outage).
/// </para>
/// </summary>
public sealed class LiveInstanceProbe : BackgroundService, ILiveInstanceProbe
{
    public const string HttpClientName = "live-instance-probe";
    public const string InstancePath = "/health/instance";
    internal static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LiveInstanceProbe> _logger;
    private readonly Uri? _probeUri;
    private volatile int _state = (int)InstanceServingState.Unknown;

    public LiveInstanceProbe(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<LiveInstanceProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        InstanceId = Guid.NewGuid().ToString("N");
        // An explicit probe URL wins, for a host whose containers cannot reach their own public address.
        var baseUrl = configuration["Deployment:LiveProbeUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = configuration["Aetheus:PublicApiBaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrl)
            && Uri.TryCreate(baseUrl.TrimEnd('/') + InstancePath, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            _probeUri = uri;
        }
    }

    public string InstanceId { get; }

    public InstanceServingState State => (InstanceServingState)_state;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_probeUri is null)
        {
            _logger.LogInformation("No public API URL to probe: this instance's serving state stays unknown");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProbeOnceAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                await Task.Delay(ProbeInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>One measurement: asks the public address which instance answers.</summary>
    internal async Task ProbeOnceAsync(CancellationToken ct)
    {
        if (_probeUri is null) return;
        InstanceServingState next;
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            var answer = await client.GetFromJsonAsync<InstanceAnswer>(_probeUri, ct).ConfigureAwait(false);
            next = string.IsNullOrWhiteSpace(answer?.InstanceId)
                ? InstanceServingState.Unknown
                : string.Equals(answer.InstanceId, InstanceId, StringComparison.Ordinal)
                    ? InstanceServingState.Live
                    : InstanceServingState.Standby;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or NotSupportedException)
        {
            if (ct.IsCancellationRequested) return;
            next = InstanceServingState.Unknown;
        }

        var previous = (InstanceServingState)Interlocked.Exchange(ref _state, (int)next);
        if (previous != next)
            _logger.LogInformation("Instance {InstanceId} serving state: {Previous} -> {Next} ({ProbeUri})",
                InstanceId, previous, next, _probeUri);
    }

    private sealed record InstanceAnswer(string? InstanceId);
}
