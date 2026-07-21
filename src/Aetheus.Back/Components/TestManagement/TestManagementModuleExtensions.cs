// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.TestManagement;

public static class TestManagementModuleExtensions
{
    public static IServiceCollection AddTestManagementModule(this IServiceCollection services)
    {
        services.AddScoped<ITestManagementRepository, TestManagementRepository>();
        services.AddScoped<ITestManagementService, TestManagementService>();
        return services;
    }
}
