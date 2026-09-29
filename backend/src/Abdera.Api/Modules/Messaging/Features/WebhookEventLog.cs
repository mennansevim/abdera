using System.Text.Json;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

// Gelen WhatsApp olayları (whatsapp_webhook_events) admin panelinde: veli yazdığında cevap
// neden gitmedi (veli bulunamadı, onay kapalı, gönderim hatası) ve Meta'nın giden mesaj için
// bildirdiği teslim hataları (statuses[].errors) yalnızca veritabanında kalıyordu.
public static class WebhookEventLog
{
    public record WebhookEventResponse(
        Guid Id, DateTimeOffset ReceivedAt, string EventType, WebhookEventStatus Status, string? ProcessingError,
        string? FromPhoneNumber, string? GuardianName, string? Text, string? DeliveryStatus, string? DeliveryError);

    public static void MapWebhookEventLog(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notifications/webhook-events", ListAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(WebhookEventStatus? status, int? page, int? pageSize, AbderaDbContext db)
    {
        var (normalizedPage, normalizedPageSize) = Pagination.Normalize(page, pageSize);

        var query = db.WhatsAppWebhookEvents.AsNoTracking();
        if (status is { } s) query = query.Where(e => e.Status == s);

        var totalCount = await query.CountAsync();
        var events = await query
            .OrderByDescending(e => e.ReceivedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        var parsed = events.Select(e => (Event: e, Payload: Parse(e.PayloadJson))).ToList();
        var phones = parsed.Select(p => p.Payload.FromPhoneNumber).OfType<string>().Distinct().ToList();
        var guardianNames = await db.Guardians.AsNoTracking()
            .Where(g => phones.Contains(g.PhoneNumber))
            .ToDictionaryAsync(g => g.PhoneNumber, g => g.FirstName + " " + g.LastName);

        return Results.Ok(new PagedResponse<WebhookEventResponse>(
            parsed.Select(p =>
            {
                string? guardianName = null;
                if (p.Payload.FromPhoneNumber is { } phone) guardianNames.TryGetValue(phone, out guardianName);
                return new WebhookEventResponse(
                    p.Event.Id, p.Event.ReceivedAt, p.Event.EventType, p.Event.Status, p.Event.ProcessingError,
                    p.Payload.FromPhoneNumber, guardianName, p.Payload.Text, p.Payload.DeliveryStatus, p.Payload.DeliveryError);
            }).ToList(), totalCount, normalizedPage, normalizedPageSize));
    }

    private record ParsedPayload(string? FromPhoneNumber, string? Text, string? DeliveryStatus, string? DeliveryError);

    private static ParsedPayload Parse(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (Webhooks.TryExtractMessage(document) is { } message)
            {
                return new ParsedPayload(message.FromPhoneNumber, message.Body, null, null);
            }

            return ParseStatus(document.RootElement);
        }
        catch (JsonException)
        {
            return new ParsedPayload(null, null, null, null);
        }
    }

    // Meta, giden mesajın akıbetini statuses[] olarak bildirir (sent/delivered/read/failed);
    // failed ise errors[] sebebi taşır (örn. 131047 "24 saatlik pencere kapalı").
    private static ParsedPayload ParseStatus(JsonElement root)
    {
        try
        {
            var value = root.GetProperty("entry")[0].GetProperty("changes")[0].GetProperty("value");
            if (!value.TryGetProperty("statuses", out var statuses) || statuses.GetArrayLength() == 0)
            {
                return new ParsedPayload(null, null, null, null);
            }

            var statusElement = statuses[0];
            var deliveryStatus = statusElement.TryGetProperty("status", out var st) ? st.GetString() : null;
            string? deliveryError = null;
            if (statusElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            {
                var error = errors[0];
                var code = error.TryGetProperty("code", out var c) ? c.ToString() : null;
                var title = error.TryGetProperty("title", out var t) ? t.GetString() : null;
                var details = error.TryGetProperty("error_data", out var data) && data.TryGetProperty("details", out var d)
                    ? d.GetString()
                    : null;
                deliveryError = string.Join(" - ", new[] { code, title, details }.Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            return new ParsedPayload(null, null, deliveryStatus, deliveryError);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return new ParsedPayload(null, null, null, null);
        }
    }
}
