// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Logs;

public static class LogsModuleExtensions
{
    public static IServiceCollection AddLogsModule(this IServiceCollection services)
    {
        services.AddScoped<ILogRepository, LogRepository>();
        services.AddScoped<ILogService, LogService>();
        return services;
    }
}
