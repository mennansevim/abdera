using System.Text.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

// docs/00-master-prompt.md webhook akışı + docs/06-whatsapp.md sequence diagram. Meta'nın
// WhatsApp Cloud API webhook JSON şekli: entry[].changes[].value.messages[] - buton tıklaması
// veya serbest metin. GET, Meta'nın abonelik doğrulama handshake'i.
public static class Webhooks
{
    public static void MapWebhooks(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/webhooks/whatsapp", VerifySubscription).AllowAnonymous();
        app.MapPost("/api/webhooks/whatsapp", ReceiveAsync).AllowAnonymous();
    }

    private static IResult VerifySubscription(HttpRequest request, IConfiguration config)
    {
        var mode = request.Query["hub.mode"].ToString();
        var token = request.Query["hub.verify_token"].ToString();
        var challenge = request.Query["hub.challenge"].ToString();
        var expectedToken = config["WhatsApp:WebhookVerifyToken"] ?? "";

        if (mode == "subscribe" && !string.IsNullOrEmpty(expectedToken) && token == expectedToken)
        {
            return Results.Text(challenge, "text/plain");
        }

        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ReceiveAsync(
        HttpRequest request, AbderaDbContext db, IClock clock, IConfiguration config, INotificationScheduler scheduler,
        IWhatsAppClient whatsAppClient)
    {
        request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(request.Body, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }
        request.Body.Position = 0;

        var signature = request.Headers["X-Hub-Signature-256"].ToString();
        var appSecret = config["WhatsApp:AppSecret"] ?? "";
        if (!WebhookSignatureVerifier.IsValid(rawBody, signature, appSecret))
        {
            // docs/00-master-prompt.md: "Validate the provider signature ... before trusting
            // the payload" - imza tutmuyorsa hiçbir şey işlenmeden reddedilir.
            return Results.Unauthorized();
        }

        await ProcessPayloadAsync(rawBody, db, clock, config, scheduler, whatsAppClient);

        // docs/06-whatsapp.md kuralı: webhook her koşulda hızlı 2xx döner.
        return Results.Ok();
    }

    internal static async Task ProcessPayloadAsync(
        string rawBody, AbderaDbContext db, IClock clock, IConfiguration config, INotificationScheduler scheduler,
        IWhatsAppClient whatsAppClient)
    {
        using var document = JsonDocument.Parse(rawBody);
        var message = TryExtractMessage(document);

        var providerEventId = message?.MessageId ?? $"no-message:{Guid.NewGuid()}";
        if (await db.WhatsAppWebhookEvents.AnyAsync(e => e.ProviderEventId == providerEventId))
        {
            // docs/06-whatsapp.md: zaten işlenmiş olay - tekrar işleme, yine de 2xx.
            return;
        }

        var now = clock.UtcNow;
        var eventRecord = WhatsAppWebhookEvent.Receive(providerEventId, message?.Type ?? "unknown", rawBody, now);
        db.WhatsAppWebhookEvents.Add(eventRecord);

        if (message is null)
        {
            // Mesaj içermeyen bir olay (örn. teslim/okundu bilgisi) - kaydedildi, işlenecek
            // bir şey yok.
            eventRecord.MarkProcessed(now);
            await db.SaveChangesAsync();
            return;
        }

        try
        {
            var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.PhoneNumber == message.FromPhoneNumber);
            if (guardian is null)
            {
                eventRecord.MarkFailed("Gönderen numaraya ait veli bulunamadı.", now);
                await db.SaveChangesAsync();
                return;
            }

            // docs/06-whatsapp.md A7: her gelen mesajda pencere +24s yenilenir.
            guardian.RefreshConversationWindow(now);
            db.WhatsAppMessages.Add(WhatsAppMessage.CreateInbound(guardian.Id, message.Body, message.MessageId, now));

            if (message.ButtonPayload is { } buttonPayload)
            {
                await HandleRsvpButtonAsync(buttonPayload, guardian.Id, db, clock, config);
            }
            else
            {
                await HandleTextMessageAsync(message.Body, guardian, db, clock, scheduler, whatsAppClient);
            }

            eventRecord.MarkProcessed(now);
        }
        catch (Exception ex)
        {
            eventRecord.MarkFailed(ex.Message, now);
        }

        await db.SaveChangesAsync();
    }

    private static async Task HandleRsvpButtonAsync(string buttonPayload, Guid guardianId, AbderaDbContext db, IClock clock, IConfiguration config)
    {
        var signingKey = config["WhatsApp:PayloadSigningKey"] ?? "";
        if (!RsvpButtonPayload.TryVerify(buttonPayload, signingKey, out var action, out var lessonId))
        {
            // docs/06-whatsapp.md: "imza tutmuyorsa istek 422 ile reddedilir" - webhook'un
            // kendisi zaten 2xx döndü, burada olay FAILED olarak loglanır (dış görünüm 422
            // değil çünkü Meta'ya tekrar denetmesi anlamsız - imza asla düzelmeyecek).
            throw new InvalidOperationException($"Buton payload imzası geçersiz: {buttonPayload}");
        }

        var response = action switch
        {
            RsvpButtonPayload.AttendingAction => RsvpResponse.Attending,
            RsvpButtonPayload.NotAttendingAction => RsvpResponse.NotAttending,
            _ => throw new InvalidOperationException($"Bilinmeyen RSVP aksiyonu: {action}"),
        };

        var rsvp = await db.LessonRsvps.SingleOrDefaultAsync(r => r.LessonId == lessonId && r.GuardianId == guardianId);
        if (rsvp is null)
        {
            rsvp = LessonRsvp.Create(lessonId, guardianId, clock.UtcNow);
            db.LessonRsvps.Add(rsvp);
        }

        rsvp.Respond(response, RsvpSource.WhatsApp, clock.UtcNow);
    }

    // docs/00-master-prompt.md deterministik intent'ler: ders/aidat/telafi/okula yaz.
    // docs/06-whatsapp.md A8: dur/iptal/stop opt-out.
    private static async Task HandleTextMessageAsync(
        string body, Abdera.Api.Modules.People.Domain.Guardian guardian, AbderaDbContext db, IClock clock,
        INotificationScheduler scheduler, IWhatsAppClient whatsAppClient)
    {
        var normalized = body.Trim().ToLowerInvariant();

        if (normalized is "dur" or "iptal" or "stop")
        {
            await HandleOptOutAsync(guardian, db, clock);
            return;
        }

        // A7: veli az önce yazdığı için pencere zaten açık (RefreshConversationWindow bu
        // mesajda çağrıldı) - serbest metin gönderimi burada güvenli.
        var responseText = await DeterministicIntents.ResolveAsync(normalized, guardian, db, clock);
        if (responseText is null) return;

        var result = await whatsAppClient.SendFreeTextAsync(guardian.PhoneNumber, responseText);
        db.WhatsAppMessages.Add(WhatsAppMessage.CreateOutbound(
            null, guardian.Id, null, responseText, result.ProviderMessageId, clock.UtcNow));
    }

    private static async Task HandleOptOutAsync(Abdera.Api.Modules.People.Domain.Guardian guardian, AbderaDbContext db, IClock clock)
    {
        var now = clock.UtcNow;
        guardian.SetNotificationConsent(false, now);

        db.AuditLogs.Add(Abdera.Api.Modules.Auth.Domain.AuditLog.Record(
            null, "guardian.opted_out", nameof(Abdera.Api.Modules.People.Domain.Guardian), guardian.Id, now));

        var pendingJobs = await db.NotificationJobs
            .Where(j => j.RecipientPhoneNumber == guardian.PhoneNumber &&
                        (j.Status == NotificationJobStatus.Pending || j.Status == NotificationJobStatus.Processing))
            .ToListAsync();
        foreach (var job in pendingJobs)
        {
            job.Cancel(now);
        }

        // docs/06-whatsapp.md: "Tek bir teyit mesajı gönderilir." Opt-out sonrası serbest
        // metin/template ayrımı önemsiz çünkü bu son mesaj - doğrudan Fake/Cloud client'a yazılır.
        db.WhatsAppMessages.Add(WhatsAppMessage.CreateOutbound(
            null, guardian.Id, null, "Bildirimleriniz durduruldu. Tekrar açmak isterseniz bize yazabilirsiniz.", null, now));
    }

    private static InboundMessage? TryExtractMessage(JsonDocument document)
    {
        try
        {
            var root = document.RootElement;
            var messagesElement = root
                .GetProperty("entry")[0]
                .GetProperty("changes")[0]
                .GetProperty("value")
                .GetProperty("messages");

            if (messagesElement.GetArrayLength() == 0) return null;

            var msg = messagesElement[0];
            var messageId = msg.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
            var from = msg.GetProperty("from").GetString() ?? "";
            var type = msg.GetProperty("type").GetString() ?? "unknown";

            string body;
            string? buttonPayload = null;
            if (type == "button" && msg.TryGetProperty("button", out var buttonEl))
            {
                buttonPayload = buttonEl.GetProperty("payload").GetString() ?? "";
                body = buttonEl.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : buttonPayload;
            }
            else if (msg.TryGetProperty("text", out var textBodyEl))
            {
                body = textBodyEl.GetProperty("body").GetString() ?? "";
            }
            else
            {
                body = "";
            }

            return new InboundMessage(messageId, PhoneNumberNormalizer.Normalize(from), type, body, buttonPayload);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private record InboundMessage(string MessageId, string FromPhoneNumber, string Type, string Body, string? ButtonPayload);
}
