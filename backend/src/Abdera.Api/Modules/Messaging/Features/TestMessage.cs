using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Infrastructure;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

// Panelden "bu numaraya deneme mesajı gönder": WhatsApp entegrasyonunun (jeton, numara kimliği, şablon)
// gerçekten çalıştığını bir veliye yazmadan görmek için. Yalnızca Admin; her gönderim audit_log'a yazılır
// (numaranın yalnızca son 4 hanesi) ve yönetici başına 10 dakikada 5 deneme ile sınırlıdır.
public static class TestMessage
{
    private const string AuditAction = "whatsapp.test_message";
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public record Request(string? PhoneNumber);

    public record Response(bool Success, string Provider, string? MessageId, string? Error);

    public static void MapTestMessage(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/whatsapp/test-message", SendAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> SendAsync(
        Request request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock, IConfiguration config,
        WhatsAppTestSender sender, CancellationToken cancellationToken)
    {
        string phone;
        try
        {
            phone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["phoneNumber"] = [ex.Message] });
        }

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var since = now - Window;
        var recent = await db.AuditLogs.CountAsync(
            a => a.ActorUserId == actorId && a.Action == AuditAction && a.CreatedAt >= since, cancellationToken);
        if (recent >= MaxAttempts)
        {
            return Results.Problem(
                title: "Çok fazla deneme",
                detail: "10 dakika içinde en fazla 5 deneme mesajı gönderilebilir.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var result = await sender.SendAsync(phone, cancellationToken);

        db.AuditLogs.Add(AuditLog.Record(
            actorId, AuditAction, "WhatsAppTestMessage", actorId, now,
            afterJson: JsonSerializer.Serialize(new { phoneEnding = phone[^4..], success = result.Success, error = result.Error })));
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new Response(
            result.Success, config["WhatsApp:Provider"] ?? "Fake", result.ProviderMessageId, result.Error));
    }
}
