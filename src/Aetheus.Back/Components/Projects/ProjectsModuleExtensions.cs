// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Projects;

public static class ProjectsModuleExtensions
{
    public static IServiceCollection AddProjectsModule(this IServiceCollection services)
    {
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IProjectService, ProjectService>();
        return services;
    }
}
