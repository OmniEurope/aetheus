// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Certbot;

public static class CertbotModuleExtensions
{
    public static IServiceCollection AddCertbotModule(this IServiceCollection services)
    {
        services.AddScoped<ICertbotRepository, CertbotRepository>();
        services.AddScoped<ICertbotService, CertbotService>();
        return services;
    }
}
