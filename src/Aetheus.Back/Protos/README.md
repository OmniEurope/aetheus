# Vendored OpenTelemetry protocol definitions

The `.proto` files below are copied from
[`open-telemetry/opentelemetry-proto` v1.10.0](https://github.com/open-telemetry/opentelemetry-proto/tree/v1.10.0)
and retain their upstream Apache-2.0 license headers. Only the stable metrics, logs, traces,
resource, common, and collector service definitions required by Aetheus are included.

`Grpc.Tools` generates C# message types at build time; no gRPC server is generated or exposed.
