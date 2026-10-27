using System.Text.Json.Nodes;

namespace Abdera.Api.Modules.Assistant.Infrastructure;

// OpenAI uyumlu /chat/completions sözleşmesi (DeepSeek de aynısını konuşur). Mesajlar ve araç
// tanımları ham JSON olarak taşınır: araç çağrılı asistan mesajı modele aynen geri gönderilmeli,
// ara bir tipe çevirip yeniden kurmak alan kaybettirir.
public interface IChatModel
{
    bool IsAvailable { get; }

    Task<ChatModelReply> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken cancellationToken);
}

// Başarılıysa Message modelin döndürdüğü asistan mesajıdır (content ve/veya tool_calls).
// Error kullanıcıya gösterilebilir bir Türkçe açıklamadır; sağlayıcının ham gövdesi değildir.
public record ChatModelReply(bool Success, JsonObject? Message, string? Error, ChatUsage? Usage = null)
{
    public static ChatModelReply Fail(string error) => new(false, null, error);
}

// Sağlayıcının bildirdiği token sayıları (maliyet takibi için loglanır). CacheHitTokens,
// PromptTokens'ın önbellekten okunan kısmıdır; sağlayıcı bildirmiyorsa 0.
public record ChatUsage(int PromptTokens, int CacheHitTokens, int CompletionTokens)
{
    public static ChatUsage Zero { get; } = new(0, 0, 0);

    public ChatUsage Add(ChatUsage? other) => other is null ? this
        : new(PromptTokens + other.PromptTokens, CacheHitTokens + other.CacheHitTokens, CompletionTokens + other.CompletionTokens);
}

public class DisabledChatModel : IChatModel
{
    public bool IsAvailable => false;

    public Task<ChatModelReply> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken cancellationToken) =>
        Task.FromResult(ChatModelReply.Fail("Asistanın dil modeli yapılandırılmamış (Ai_Chatbot_ApiKey)."));
}
