// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.WorkItems;

public static class WorkItemsModuleExtensions
{
    public static IServiceCollection AddWorkItemsModule(this IServiceCollection services)
    {
        services.AddScoped<IWorkItemRepository, WorkItemRepository>();
        services.AddScoped<IWorkItemService, WorkItemService>();
        return services;
    }
}
