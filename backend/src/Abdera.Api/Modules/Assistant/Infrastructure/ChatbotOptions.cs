namespace Abdera.Api.Modules.Assistant.Infrastructure;

// Asistanın dil modeli (docs/10-decisions.md AE9). .env'deki düz adlar okunur
// (Ai_Chatbot_Provider, Ai_Chatbot_ApiKey; isteğe bağlı Ai_Chatbot_Model, Ai_Chatbot_BaseUrl);
// appsettings/test için "AiChatbot:*" bölümü de geçerlidir. Gelişim yorumunun Ai__* ayarlarından
// ayrıdır: o OpenAI, bu DeepSeek.
//
// Sağlayıcı DeepSeek ya da OpenAI uyumlu herhangi bir uç olabilir; adres ve model sağlayıcıya
// göre varsayılanla dolar. Anahtar yoksa asistan kapalıdır ve açıkça hata döner (kural yoluna
// düşmez - kullanıcı kararı).
public class ChatbotOptions
{
    public string Provider { get; set; } = "Disabled";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 45;
    // Tek mesajda model en fazla bu kadar araç turu atabilir; sonsuz döngüye karşı.
    public int MaxToolRounds { get; set; } = 5;

    public bool IsDeepSeek => BaseUrl.Contains("deepseek.com", StringComparison.OrdinalIgnoreCase);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ApiKey) && !string.Equals(Provider, "Disabled", StringComparison.OrdinalIgnoreCase);

    public static ChatbotOptions FromConfiguration(IConfiguration configuration)
    {
        string? Read(string flat, string section) =>
            NonEmpty(configuration[flat]) ?? NonEmpty(configuration[$"AiChatbot:{section}"]);

        var apiKey = Read("Ai_Chatbot_ApiKey", "ApiKey") ?? "";
        // Anahtar verilmiş ama sağlayıcı yazılmamışsa ya da tanınmıyorsa DeepSeek varsayılır.
        var provider = Read("Ai_Chatbot_Provider", "Provider") ?? (apiKey.Length > 0 ? "DeepSeek" : "Disabled");
        var isOpenAi = provider.Contains("openai", StringComparison.OrdinalIgnoreCase);

        return new ChatbotOptions
        {
            Provider = provider,
            ApiKey = apiKey,
            BaseUrl = Read("Ai_Chatbot_BaseUrl", "BaseUrl") ?? (isOpenAi ? "https://api.openai.com/v1" : "https://api.deepseek.com"),
            // "deepseek-chat" eski bir ad (bugün düşünmesiz deepseek-flash'a gidiyor); kalkarsa asistan
            // durmasın diye güncel ad kullanılır, düşünme modu istekte kapatılır (IsDeepSeek).
            Model = Read("Ai_Chatbot_Model", "Model") ?? (isOpenAi ? "gpt-4o-mini" : "deepseek-flash"),
            TimeoutSeconds = int.TryParse(Read("Ai_Chatbot_TimeoutSeconds", "TimeoutSeconds"), out var timeout) ? timeout : 45,
        };
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
