// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;

namespace Aetheus.Back.Components.Vaults;

public static class VaultsModuleExtensions
{
    public static IServiceCollection AddVaultsModule(this IServiceCollection services)
    {
        services.AddScoped<IVaultRepository, VaultRepository>();
        services.AddScoped<IVaultService, VaultService>();
        services.AddHostedService<SecretExpirationService>();
        return services;
    }
}
