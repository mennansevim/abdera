using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.Messaging.Infrastructure;
using Abdera.Api.Shared.Modules;

namespace Abdera.Api.Modules.Messaging;

public static class MessagingModule
{
    public static void AddMessagingModule(this IServiceCollection services)
    {
        services.AddScoped<INotificationScheduler, NotificationScheduler>();
        services.AddScoped<IStaffNotifier, StaffNotifier>();
        services.AddHostedService<NotificationDispatcher>();
    }

    public static void MapMessagingModule(this WebApplication app)
    {
        // Ekran içi personel bildirimleri çekirdek; WhatsApp tarafı (webhook, Mesaj Merkezi,
        // otomasyon ayarları, geliştirme simülatörü) "whatsapp" modülüdür.
        app.MapStaffNotifications();

        var whatsApp = app.MapModule(ModuleKeys.WhatsApp);
        whatsApp.MapWebhooks();
        whatsApp.MapNotifications();
        whatsApp.MapWebhookEventLog();
        whatsApp.MapAutomationSettings();

        if (app.Environment.IsDevelopment())
        {
            whatsApp.MapDevWhatsAppSimulator();
        }
    }
}
