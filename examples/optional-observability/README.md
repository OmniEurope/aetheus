<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Optional Aetheus observability

The default build has no Aetheus package reference and needs only public NuGet sources:

```bash
dotnet build
```

The private deployment pipeline can inject its authenticated feed and independently enable either
product:

```bash
dotnet build -p:EnableAetheusTelemetry=true
dotnet build -p:EnableAetheusWebAnalytics=true
dotnet build -p:EnableAetheusTelemetry=true -p:EnableAetheusWebAnalytics=true
```

Runtime emission remains independently disabled unless `AETHEUS_TELEMETRY_ENABLED=true` or
`AETHEUS_WEB_ANALYTICS_ENABLED=true` is provided. Feed credentials belong in the private pipeline
secret store and must never be written to this project.
