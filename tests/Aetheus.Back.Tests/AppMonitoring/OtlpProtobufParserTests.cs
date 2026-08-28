// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.AppMonitoring.Ingest;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Aetheus.Back.Tests.AppMonitoring;

public class OtlpProtobufParserTests
{
    [Fact]
    public void ParseMetrics_ProjectsGaugeAndAttributesWithoutJsonRoundTrip()
    {
        var request = new ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                {
                    ScopeMetrics =
                    {
                        new ScopeMetrics
                        {
                            Metrics =
                            {
                                new Metric
                                {
                                    Name = "http.duration",
                                    Unit = "ms",
                                    Gauge = new Gauge
                                    {
                                        DataPoints =
                                        {
                                            new NumberDataPoint
                                            {
                                                TimeUnixNano = 1_700_000_000_000_000_000,
                                                AsDouble = 12.5,
                                                Attributes =
                                                {
                                                    new KeyValue
                                                    {
                                                        Key = "method",
                                                        Value = new AnyValue { StringValue = "GET" }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var metric = Assert.Single(OtlpProtobufParser.ParseMetrics(request.ToByteArray()));

        Assert.Equal("http.duration", metric.MetricName);
        Assert.Equal(12.5, metric.Value);
        Assert.Equal("ms", metric.Unit);
        Assert.Contains("\"method\":\"GET\"", metric.AttributesJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseMetrics_OversizedAttributesRemainValidBoundedJson()
    {
        var point = new NumberDataPoint
        {
            TimeUnixNano = 1_700_000_000_000_000_000,
            AsDouble = 12.5,
            Attributes =
            {
                new KeyValue
                {
                    Key = "oversized",
                    Value = new AnyValue { StringValue = new string('x', 2048) }
                }
            }
        };
        var request = CreateMetricRequest(new Metric
        {
            Name = "http.duration",
            Gauge = new Gauge { DataPoints = { point } }
        });

        var metric = Assert.Single(OtlpProtobufParser.ParseMetrics(request.ToByteArray()));

        Assert.NotNull(metric.AttributesJson);
        Assert.InRange(metric.AttributesJson.Length, 1, 1024);
        using var attributes = JsonDocument.Parse(metric.AttributesJson);
        Assert.Equal(
            bool.TrueString,
            attributes.RootElement.GetProperty("_aetheus_truncated").GetString());
    }

    [Fact]
    public void ParseMetrics_OverflowingHistogramBucketCountsAreRejected()
    {
        var point = new HistogramDataPoint
        {
            TimeUnixNano = 1_700_000_000_000_000_000,
            BucketCounts = { ulong.MaxValue, 1 },
            ExplicitBounds = { 10 }
        };
        var request = CreateMetricRequest(new Metric
        {
            Name = "http.duration",
            Histogram = new Histogram { DataPoints = { point } }
        });

        Assert.Empty(OtlpProtobufParser.ParseMetrics(request.ToByteArray()));
    }

    [Fact]
    public void ParseLogs_PreservesCanonicalProtobufSeverity()
    {
        var request = new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            LogRecords =
                            {
                                new LogRecord
                                {
                                    TimeUnixNano = 1_700_000_000_000_000_000,
                                    SeverityNumber = SeverityNumber.Error,
                                    SeverityText = "ERROR",
                                    Body = new AnyValue { StringValue = "boom" }
                                }
                            }
                        }
                    }
                }
            }
        };

        var log = Assert.Single(OtlpProtobufParser.ParseLogs(request.ToByteArray()));

        Assert.Equal(17, log.SeverityNumber);
        Assert.Equal("ERROR", log.SeverityText);
        Assert.Equal("boom", log.Body);
    }

    [Fact]
    public void ParseErrors_RecognizesCanonicalProtobufErrorStatus()
    {
        var request = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Spans =
                            {
                                new Span
                                {
                                    Name = "GET /failed",
                                    EndTimeUnixNano = 1_700_000_000_000_000_000,
                                    Status = new Status { Code = Status.Types.StatusCode.Error }
                                }
                            }
                        }
                    }
                }
            }
        };

        var error = Assert.Single(OtlpProtobufParser.ParseErrors(request.ToByteArray()));

        Assert.Equal("SpanError", error.ExceptionType);
        Assert.Equal("GET /failed", error.Message);
    }

    private static ExportMetricsServiceRequest CreateMetricRequest(Metric metric) =>
        new()
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                {
                    ScopeMetrics =
                    {
                        new ScopeMetrics { Metrics = { metric } }
                    }
                }
            }
        };
}
