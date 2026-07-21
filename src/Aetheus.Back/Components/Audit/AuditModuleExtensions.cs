// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Audit;

public static class AuditModuleExtensions
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IAuditChainService, AuditChainService>();
        services.AddScoped<IAuditService, AuditService>();
        return services;
    }
}
