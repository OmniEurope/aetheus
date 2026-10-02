// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Telemetry;

/// <summary>
/// Which requests <see cref="RequestPerformanceRecorder"/> keeps. Configured through
/// <see cref="AetheusRequestPerformanceExtensions.AddAetheusRequestPerformance"/>; every call adds its
/// configuration, so the order in which the host and <c>AddAetheusTelemetry</c> register does not matter.
/// </summary>
public sealed class RequestPerformanceOptions
{
    /// <summary>
    /// Decides whether a route template (<c>api/orders/{id}</c>, no leading slash) is measured. Null keeps
    /// every templated route except the health probes and static assets excluded from tracing.
    /// </summary>
    public Func<string, bool>? RouteFilter { get; set; }

    /// <summary>
    /// Route templates whose samples are recorded but do not complete
    /// <see cref="RequestPerformanceRecorder.WaitForChangeAsync"/>: the route of a page that reads the
    /// report would otherwise announce a change of it on every read.
    /// </summary>
    public ISet<string> QuietRoutes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
