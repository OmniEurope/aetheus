// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Components.PersonalAccessTokens;

public static class PersonalAccessTokensModuleExtensions
{
    public static IServiceCollection AddPersonalAccessTokensModule(this IServiceCollection services)
    {
        services.AddScoped<IPersonalAccessTokenRepository, PersonalAccessTokenRepository>();
        services.AddScoped<IPersonalAccessTokenService, PersonalAccessTokenService>();
        return services;
    }
}
