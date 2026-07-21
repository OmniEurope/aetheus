// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.SystemLogs;

public static class SystemLogsModuleExtensions
{
    public static IServiceCollection AddSystemLogsModule(this IServiceCollection services)
    {
        services.AddSingleton<ISystemLogService, SystemLogService>();
        return services;
    }
}
