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
