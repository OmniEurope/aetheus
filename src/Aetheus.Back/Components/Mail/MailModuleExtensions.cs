// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Components.Mail;

public static class MailModuleExtensions
{
    public static IServiceCollection AddMailModule(this IServiceCollection services)
    {
        services.AddScoped<IMailRepository, MailRepository>();
        services.AddKeyedScoped<IMailService, MailService>("inner");
        services.AddScoped<IMailService>(sp =>
            new CachedMailService(
                sp.GetRequiredKeyedService<IMailService>("inner"),
                sp.GetRequiredService<IMemoryCache>()));
        return services;
    }
}
