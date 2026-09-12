using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.Messaging.Infrastructure;

namespace Abdera.Api.Modules.Messaging;

public static class MessagingModule
{
    public static void AddMessagingModule(this IServiceCollection services, bool enableHostedServices = true)
    {
        services.AddScoped<INotificationScheduler, NotificationScheduler>();
        services.AddScoped<IStaffNotifier, StaffNotifier>();
        if (enableHostedServices)
        {
            services.AddHostedService<NotificationDispatcher>();
        }
    }

    public static void MapMessagingModule(this WebApplication app)
    {
        app.MapWebhooks();
        app.MapNotifications();
        app.MapStaffNotifications();
        app.MapMessageTemplates();
        app.MapAutomationSettings();

        if (app.Environment.IsDevelopment())
        {
            app.MapDevWhatsAppSimulator();
        }
    }
}
