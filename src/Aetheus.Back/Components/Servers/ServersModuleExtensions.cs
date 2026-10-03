// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Servers;

public static class ServersModuleExtensions
{
    public static IServiceCollection AddServersModule(this IServiceCollection services)
    {
        services.AddScoped<IServerRepository, ServerRepository>();
        // Same scope as IServerRepository on purpose: both resolve the SAME AppDbContext, which is
        // what keeps one heartbeat one transaction after the split.
        services.AddScoped<IServerHeartbeatRepository, ServerHeartbeatRepository>();
        services.AddScoped<IServerRetirementRepository, ServerRetirementRepository>();
        services.AddScoped<IServerRetirementService, ServerRetirementService>();
        services.AddScoped<ServerService>();
        // Segregated facets resolve to the same instance - consumers depend on the narrowest surface.
        services.AddScoped<IServerLifecycleService>(sp => sp.GetRequiredService<ServerService>());
        services.AddScoped<IServerHeartbeatService>(sp => sp.GetRequiredService<ServerService>());
        services.AddScoped<IServerServiceManagementService>(sp => sp.GetRequiredService<ServerService>());
        services.AddScoped<IServerAgentContactService>(sp => sp.GetRequiredService<ServerService>());
        services.AddScoped<IServerDiagnosticService>(sp => sp.GetRequiredService<ServerService>());
        services.AddHostedService<ServerTimeoutService>();
        // Audit observer and admin toast of this module's ServerWentOffline event (moved from the Shared
        // module, 2026-09-25).
        services.AddScoped<IDomainEventHandler<Events.ServerWentOfflineEvent>, DomainEventAuditHandler<Events.ServerWentOfflineEvent>>();
        services.AddScoped<IDomainEventHandler<Events.ServerWentOfflineEvent>, Handlers.ServerOfflineNotificationHandler>();
        // Audit R2-023 follow-up: a sudoers drift is kept in the audit trail and the administrators'
        // notifications, not only pushed to the browsers connected when it is seen.
        services.AddScoped<IDomainEventHandler<Events.SudoersDriftDetectedEvent>, DomainEventAuditHandler<Events.SudoersDriftDetectedEvent>>();
        services.AddScoped<IDomainEventHandler<Events.SudoersDriftDetectedEvent>, Handlers.SudoersDriftNotificationHandler>();
        return services;
    }
}
