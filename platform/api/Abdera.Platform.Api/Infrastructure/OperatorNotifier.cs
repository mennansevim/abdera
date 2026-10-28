using System.Net.Http.Json;

namespace Abdera.Platform.Api.Infrastructure;

// Panelin dışında operatöre ulaşan bildirim: alarm yalnızca panelde durursa gece düşen kritik bir sorun
// sabaha kadar görülmez. Tek kanal, genel bir webhook (Slack/Discord/Telegram köprüsü); kişi verisi taşımaz,
// yalnızca okul kısaltması, tür, düzey ve alarm metni gider.
public record OperatorNotice(string Level, string SchoolSlug, string Kind, string Detail);

public interface IOperatorNotifier
{
    // En iyi çaba: bildirimin gitmemesi alarmı kaydeden işlemi asla bozmaz.
    Task NotifyAsync(IReadOnlyCollection<OperatorNotice> notices, CancellationToken cancellationToken);
}

public class WebhookOperatorNotifier(IHttpClientFactory clients, IConfiguration config, ILogger<WebhookOperatorNotifier> logger) : IOperatorNotifier
{
    public const string HttpClientName = "operator-notifier";

    public async Task NotifyAsync(IReadOnlyCollection<OperatorNotice> notices, CancellationToken cancellationToken)
    {
        var url = config["Notifications:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(url) || notices.Count == 0) return;
        var minimum = Rank(config["Notifications:MinLevel"] is { Length: > 0 } configured ? configured : "critical");
        var selected = notices.Where(n => Rank(n.Level) >= minimum).ToList();
        if (selected.Count == 0) return;

        var text = string.Join("\n", selected.Select(n => $"[{n.Level.ToUpperInvariant()}] {n.SchoolSlug} · {n.Kind}: {n.Detail}"));
        try
        {
            using var client = clients.CreateClient(HttpClientName);
            // "text" Slack, "content" Discord için; ikisini de gönderen basit bir köprü her ikisini de tanır.
            using var response = await client.PostAsJsonAsync(url, new { text, content = text }, cancellationToken);
            if (!response.IsSuccessStatusCode) logger.LogWarning("Operatör bildirimi reddedildi: {Status}", (int)response.StatusCode);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Operatör bildirimi gönderilemedi: {Error}", ex.Message);
        }
    }

    private static int Rank(string level) => level.ToLowerInvariant() switch { "critical" => 2, "warning" => 1, _ => 0 };
}
