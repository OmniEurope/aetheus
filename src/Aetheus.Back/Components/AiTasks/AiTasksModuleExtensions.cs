// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.AiTasks;

public static class AiTasksModuleExtensions
{
    public static IServiceCollection AddAiTasksModule(this IServiceCollection services)
    {
        services.AddScoped<IAiTaskRepository, AiTaskRepository>();
        // Reactive: Notifications states that a product event happened, this module decides whether it
        // starts an AI task. Observer dispatch, so a trigger failure cannot stop a notification.
        services.AddScoped<
            Services.DomainEvents.IDomainEventHandler<Notifications.Events.NotificationEventRaisedEvent>,
            NotificationAiTaskTriggerHandler>();
        services.AddScoped<AiTaskService>();
        services.AddScoped<IAiTaskService>(provider => provider.GetRequiredService<AiTaskService>());
        services.AddHostedService<AiTaskSchedulerService>();
        return services;
    }
}
