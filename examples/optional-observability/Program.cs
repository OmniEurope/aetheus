// SPDX-License-Identifier: EUPL-1.2
#if AETHEUS_TELEMETRY
using Aetheus.Telemetry;
#endif
#if AETHEUS_WEB_ANALYTICS
using Aetheus.WebAnalytics;
#endif

var builder = WebApplication.CreateBuilder(args);

#if AETHEUS_TELEMETRY
builder.Services.AddAetheusTelemetry(builder.Configuration);
#endif
#if AETHEUS_WEB_ANALYTICS
builder.Services.AddAetheusWebAnalytics(builder.Configuration);
#endif

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

#if AETHEUS_WEB_ANALYTICS
app.UseStaticFiles();
app.MapAetheusWebAnalytics();
#endif

app.Run();
