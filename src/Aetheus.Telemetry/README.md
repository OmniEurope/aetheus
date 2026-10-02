# Aetheus.Telemetry

`Aetheus.Telemetry` is an optional convenience layer over the official OpenTelemetry .NET SDK. It emits
standard OTLP/HTTP protobuf, so an application using only official OpenTelemetry packages is equivalent.

The `.nupkg` is distributed exclusively through the internal Aetheus NuGet registry. Configure the
private source and its runtime credential as documented in
[`docs/runbooks/package-registry.md`](../../docs/runbooks/package-registry.md); it is never published to
nuget.org (ADR-037).

```csharp
builder.Services.AddAetheusTelemetry(builder.Configuration);
```

Configuration lives under `Aetheus:Telemetry`. `Enabled` defaults to `false` and the
`AETHEUS_TELEMETRY_ENABLED` environment variable has final say. Required values when enabled are the
application name/id, the metrics/traces HTTPS endpoints, and the ingestion key. Logs have their own
switch, `EnableLogs` (environment variable `AETHEUS_TELEMETRY_LOGS_ENABLED`), and are disabled until
it is set; their endpoint is required only when they are enabled. Export uses bounded batches, a
three-second timeout, sampling, and never blocks a request on network I/O.

| Runtime | OpenTelemetry | Support |
| --- | --- | --- |
| .NET 8 LTS | 1.17.x | tested minimum |
| .NET 10 | 1.17.x | tested current |

## Request performance (0.2.0, window and lower-case routes in 1.0.0)

When telemetry is enabled, the package also measures, in process, how long each route takes to answer.
It listens to the ASP.NET Core `http.server.request.duration` instrument, keeps the last 24 hours of
requests in memory (50,000 at most, grouped by route template: no concrete path, identifier or query
value, in lower case so one route is one line whatever the case of its source), and exports five gauges
on the meter `Aetheus.Telemetry.Performance`, one series per `http.route` and `http.request.method`:

| Metric | Unit | Value |
| --- | --- | --- |
| `aetheus.http.server.request.count` | `{request}` | requests of the window |
| `aetheus.http.server.request.duration.p50` | `ms` | median (nearest rank) |
| `aetheus.http.server.request.duration.p95` | `ms` | 95th percentile (nearest rank) |
| `aetheus.http.server.request.duration.p99` | `ms` | 99th percentile (nearest rank) |
| `aetheus.http.server.request.duration.max` | `ms` | slowest request |

A sixth gauge, `aetheus.http.server.request.window` (`s`, no attribute), says how many seconds the kept
requests span: 24 hours at most, less after a restart. A percentile is a request that happened (nearest
rank, no interpolation), so below 20 requests the 95th percentile is the maximum, and below 100 the 99th.

Aetheus shows them in the Performance tab of the application's Supervision. The window restarts with
the process. Health probes and static assets are not measured. A host that wants the figures without
the export calls `AddAetheusRequestPerformance()` and reads `RequestPerformanceRecorder` itself.

The package composes with additional official instrumentations configured by the host. Both registration
paths use the same `OpenTelemetryBuilder`, so an application may call `AddOpenTelemetry()` before or after
the Aetheus helper without creating a proprietary provider. Calling `AddAetheusTelemetry` more than once
is idempotent.

The equivalent language-neutral resource attributes are `service.name`, `service.version`,
`deployment.environment.name`, and `aetheus.application.id`. See the Aetheus monitoring runbook for the
manual SDK configuration.

An application that does not want the Aetheus package can send the same contract with official packages:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("portfolio", serviceVersion: "1.2.3")
        .AddAttributes([
            new("deployment.environment.name", "production"),
            new("aetheus.application.id", 42)
        ]))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());
```

Set the standard `OTEL_EXPORTER_OTLP_*_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`, and
`OTEL_EXPORTER_OTLP_HEADERS=x-aetheus-ingest-key=<key>` environment variables. The same OTLP resource
contract works for Node.js, Java, Python, and other official OpenTelemetry SDKs.
