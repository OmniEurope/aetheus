// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services;

namespace Aetheus.Back.Components.Auth;

public static class AuthModuleExtensions
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services)
    {
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<ITotpService, TotpService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IServerEnrollmentService, ServerEnrollmentService>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddHostedService<RefreshTokenCleanupService>();
        return services;
    }
}
