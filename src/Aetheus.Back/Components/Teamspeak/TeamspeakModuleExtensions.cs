// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Teamspeak;

public static class TeamspeakModuleExtensions
{
    public static IServiceCollection AddTeamspeakModule(this IServiceCollection services)
    {
        services.AddScoped<ITeamspeakRepository, TeamspeakRepository>();
        services.AddScoped<ITeamspeakService, TeamspeakService>();
        return services;
    }
}
