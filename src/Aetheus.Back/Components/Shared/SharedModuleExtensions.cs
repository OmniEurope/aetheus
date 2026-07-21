// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines.Events;
using Aetheus.Back.Components.Servers.Events;
using Aetheus.Back.Components.Servers.Handlers;
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
        services.AddScoped<ValidateServerExistsFilter>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddHostedService<BackgroundTaskQueueHostedService>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // Generic audit observers: every registered event type is auto-recorded in the audit trail.
        services.AddScoped<IDomainEventHandler<PipelineRunCompletedEvent>, DomainEventAuditHandler<PipelineRunCompletedEvent>>();
        services.AddScoped<IDomainEventHandler<ServerWentOfflineEvent>, DomainEventAuditHandler<ServerWentOfflineEvent>>();
        services.AddScoped<IDomainEventHandler<PipelineApprovalRequestedEvent>, DomainEventAuditHandler<PipelineApprovalRequestedEvent>>();

        // Real-time toast for admins: ServerOffline broadcast on AlertHub.
        services.AddScoped<IDomainEventHandler<ServerWentOfflineEvent>, ServerOfflineNotificationHandler>();
        return services;
    }
}
