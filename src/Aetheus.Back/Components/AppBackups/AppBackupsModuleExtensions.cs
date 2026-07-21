// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Components.AppBackups;

public static class AppBackupsModuleExtensions
{
    public static IServiceCollection AddAppBackupsModule(this IServiceCollection services)
    {
        services.AddScoped<IBackupRepository, BackupRepository>();
        services.AddScoped<IBackupPolicyService, BackupPolicyService>();
        services.AddHostedService<BackupSchedulerService>();
        return services;
    }
}
