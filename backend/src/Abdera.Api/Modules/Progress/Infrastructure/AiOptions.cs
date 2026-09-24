namespace Abdera.Api.Modules.Progress.Infrastructure;

public class AiOptions
{
    public string Provider { get; set; } = "Disabled"; // Disabled | OpenAi
    public string ApiKey { get; set; } = "";

    // OpenAI uyumlu herhangi bir uç nokta kullanılabilir (OpenAI, Azure OpenAI, kendi
    // gateway'in) - bu yüzden adres sabit değil, konfigürasyondan gelir.
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";

    // Yorum gelişim ekranı açılırken üretilir; sağlayıcı yavaşsa ekran süresiz beklemesin.
    public int TimeoutSeconds { get; set; } = 20;
}
