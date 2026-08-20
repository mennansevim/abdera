using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.Messaging.Infrastructure;

namespace Abdera.Api.Modules.Messaging;

public static class MessagingModule
{
    public static void AddMessagingModule(this IServiceCollection services)
    {
        services.AddScoped<INotificationScheduler, NotificationScheduler>();
        services.AddHostedService<NotificationDispatcher>();
    }

    public static void MapMessagingModule(this WebApplication app)
    {
        app.MapWebhooks();
        app.MapNotifications();

        if (app.Environment.IsDevelopment())
        {
            app.MapDevWhatsAppSimulator();
        }
    }
}
