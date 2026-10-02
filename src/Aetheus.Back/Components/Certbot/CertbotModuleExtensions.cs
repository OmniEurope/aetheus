// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Certbot;

public static class CertbotModuleExtensions
{
    public static IServiceCollection AddCertbotModule(this IServiceCollection services)
    {
        services.AddScoped<ICertbotRepository, CertbotRepository>();
        services.AddScoped<ICertbotService, CertbotService>();
        services.AddScoped<IDomainEventHandler<CertbotRenewalCheckFailedEvent>, CertbotRenewalCheckNotificationHandler>();
        return services;
    }
}
