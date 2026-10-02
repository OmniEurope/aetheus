// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Shared;

public static class SharedModuleExtensions
{
    /// <summary>
    /// Registers cross-module filters and helpers used by multiple feature modules
    /// (Apache, Certbot, Docker, Mail, Cron, Teamspeak, Portsentry, Rkhunter, ...).
    /// Should be called once from <c>Program.cs</c> instead of being duplicated in each
    /// module's <c>Add{Module}Module()</c>.
    /// </summary>
    public static IServiceCollection AddSharedModule(this IServiceCollection services)
    {
        services.AddScoped<ServerExistenceRepository>();
        services.AddScoped<ValidateServerExistsFilter>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddHostedService<BackgroundTaskQueueHostedService>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // The audit observers of each module's events are registered by that module (Pipelines, Servers):
        // this one sits below every module and names none of them (layer guard, 2026-09-25).
        return services;
    }
}
