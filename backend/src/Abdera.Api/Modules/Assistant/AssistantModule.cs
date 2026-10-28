using Abdera.Api.Modules.Assistant.Features;
using Abdera.Api.Modules.Assistant.Infrastructure;
using Abdera.Api.Shared.Modules;

namespace Abdera.Api.Modules.Assistant;

// Uygulama içi asistan (docs/10-decisions.md AE, AE9). Dil modeli DeepSeek (Ai_Chatbot_*);
// anahtar yoksa asistan kapalıdır ve açık hata döner. Tablo ve arka plan işi yoktur; sohbet
// sunucuda saklanmaz.
public static class AssistantModule
{
    public static void AddAssistantModule(this IServiceCollection services, IConfiguration configuration)
    {
        var options = ChatbotOptions.FromConfiguration(configuration);
        services.AddSingleton(options);
        if (options.IsEnabled) services.AddHttpClient<IChatModel, OpenAiCompatibleChatModel>();
        else services.AddSingleton<IChatModel, DisabledChatModel>();
    }

    public static void MapAssistantModule(this WebApplication app)
    {
        // Yapay zekâ modülünün parçası: modül kapalı okulda uç 404 döner (multi-tenant §4).
        app.MapModule(ModuleKeys.Ai).MapAssistantChat();
    }
}
