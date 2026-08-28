// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Aetheus.Telemetry;

public static class AetheusTelemetryBuilderExtensions
{
    private const string EnabledEnvironmentVariable = "AETHEUS_TELEMETRY_ENABLED";

    public static IServiceCollection AddAetheusTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AetheusTelemetryOptions>? configure = null)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AetheusTelemetryMarker)))
            return services;

        var options = ReadOptions(configuration);
        configure?.Invoke(options);

        var enabledOverride = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable);
        if (bool.TryParse(enabledOverride, out var enabled))
            options.Enabled = enabled;

        options.Validate();
        services.AddSingleton<AetheusTelemetryMarker>();
        services.AddSingleton(options);

        if (!options.Enabled)
            return services;

        services.Configure<BatchExportActivityProcessorOptions>(processor =>
        {
            processor.MaxQueueSize = options.MaxExportQueueSize;
            processor.MaxExportBatchSize = options.MaxExportBatchSize;
            processor.ExporterTimeoutMilliseconds = options.ExportTimeoutMilliseconds;
        });

        var openTelemetry = services.AddOpenTelemetry().ConfigureResource(builder => builder
            .AddService(options.ApplicationName, serviceVersion: options.ApplicationVersion)
            .AddAttributes(
            [
                new KeyValuePair<string, object>("deployment.environment.name", options.EnvironmentName),
                new KeyValuePair<string, object>("aetheus.application.id", options.ApplicationId)
            ]));

        openTelemetry.WithTracing(builder =>
        {
            builder
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(options.TraceSampleRatio)))
                .AddProcessor(new SensitiveActivityProcessor())
                .AddAspNetCoreInstrumentation(instrumentation =>
                {
                    // Exception messages and stacks routinely contain URLs, payload values, or
                    // secrets. Error status and exception type remain observable without them.
                    instrumentation.RecordException = false;
                    instrumentation.Filter = context =>
                        !TelemetryRoutePolicy.IsExcludedPath(context.Request.Path.Value);
                });

            if (options.EnableHttpClientTracing)
                builder.AddHttpClientInstrumentation();

            builder.AddOtlpExporter(exporter =>
                ConfigureExporter(exporter, options.TracesEndpoint!, options));
        });

        openTelemetry.WithMetrics(builder =>
        {
            builder.AddAspNetCoreInstrumentation();
            if (options.EnableHttpClientTracing)
                builder.AddHttpClientInstrumentation();
            if (options.EnableRuntimeMetrics)
                builder.AddRuntimeInstrumentation();

            builder.AddOtlpExporter(exporter => ConfigureExporter(exporter, options.MetricsEndpoint!, options));
        });

        if (options.EnableLogs)
        {
            services.AddLogging(builder => builder.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = false;
                logging.IncludeScopes = true;
                logging.ParseStateValues = true;
                logging.AddOtlpExporter((exporter, processor) =>
                {
                    ConfigureExporter(exporter, options.LogsEndpoint!, options);
                    processor.BatchExportProcessorOptions.MaxQueueSize = options.MaxExportQueueSize;
                    processor.BatchExportProcessorOptions.MaxExportBatchSize = options.MaxExportBatchSize;
                    processor.BatchExportProcessorOptions.ExporterTimeoutMilliseconds =
                        options.ExportTimeoutMilliseconds;
                });
            }));
        }

        return services;
    }

    private static bool ReadBool(string? raw, bool fallback) =>
        bool.TryParse(raw, out var value) ? value : fallback;

    private static AetheusTelemetryOptions ReadOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Aetheus:Telemetry");
        return new AetheusTelemetryOptions
        {
            Enabled = section.GetValue("Enabled", false),
            ApplicationName = section["ApplicationName"]
                              ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
                              ?? string.Empty,
            ApplicationVersion = section["ApplicationVersion"],
            EnvironmentName = section["EnvironmentName"] ?? "production",
            ApplicationId = ReadInt(
                section["ApplicationId"],
                Environment.GetEnvironmentVariable("AETHEUS_TELEMETRY_APPLICATION_ID")),
            MetricsEndpoint = ReadUri(
                section["MetricsEndpoint"]
                ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT")),
            LogsEndpoint = ReadUri(
                section["LogsEndpoint"]
                ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT")),
            TracesEndpoint = ReadUri(
                section["TracesEndpoint"]
                ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")),
            IngestKey = section["IngestKey"] ?? ReadIngestKeyFromOtlpHeaders(),
            // Logs (and the error list derived from them) stay opt-in, but the deployment must be able
            // to turn them on the same way it turns telemetry on: through an injected variable rather
            // than an appsettings edit inside the image.
            EnableLogs = ReadBool(
                section["EnableLogs"]
                ?? Environment.GetEnvironmentVariable("AETHEUS_TELEMETRY_LOGS_ENABLED"),
                false),
            EnableRuntimeMetrics = section.GetValue("EnableRuntimeMetrics", true),
            EnableHttpClientTracing = section.GetValue("EnableHttpClientTracing", true),
            TraceSampleRatio = section.GetValue("TraceSampleRatio", 0.1),
            ExportTimeoutMilliseconds = section.GetValue("ExportTimeoutMilliseconds", 3000),
            MaxExportQueueSize = section.GetValue("MaxExportQueueSize", 2048),
            MaxExportBatchSize = section.GetValue("MaxExportBatchSize", 512)
        };
    }

    private static Uri? ReadUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private static int ReadInt(string? configured, string? environmentValue) =>
        int.TryParse(
            configured ?? environmentValue,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    private static string ReadIngestKeyFromOtlpHeaders()
    {
        var headers = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS");
        if (string.IsNullOrWhiteSpace(headers))
            return string.Empty;
        foreach (var pair in headers.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0
                && string.Equals(
                    pair[..separator].Trim(),
                    "x-aetheus-ingest-key",
                    StringComparison.OrdinalIgnoreCase))
                return pair[(separator + 1)..].Trim();
        }
        return string.Empty;
    }

    private static void ConfigureExporter(
        OtlpExporterOptions exporter,
        Uri endpoint,
        AetheusTelemetryOptions options)
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
        exporter.Headers = $"x-aetheus-ingest-key={options.IngestKey}";
        exporter.TimeoutMilliseconds = options.ExportTimeoutMilliseconds;
    }

    private sealed class AetheusTelemetryMarker;
}
