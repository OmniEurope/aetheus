// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Telemetry;

namespace Aetheus.Back.Components.SystemLogs;

public static class SystemLogsModuleExtensions
{
    public static IServiceCollection AddSystemLogsModule(this IServiceCollection services)
    {
        services.AddSingleton<ISystemLogService, SystemLogService>();
        // PLAN-003 lot 27, R-455: one recorder, the Aetheus.Telemetry package's, started with the host so
        // it hears the first request. Registered here whether or not the OTLP export is enabled.
        services.AddAetheusRequestPerformance(ApiPerformanceReport.Configure);
        // Recette R-181: the system logs page follows the log files over the admin hub.
        services.AddHostedService<SystemLogChangeBroadcaster>();
        // R-181: tells the open Performance pages when new samples arrived (coalesced, no timer poll).
        services.AddHostedService<ApiPerformanceChangeBroadcaster>();
        return services;
    }
}
