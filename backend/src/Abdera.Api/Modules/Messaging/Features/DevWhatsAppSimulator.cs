using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Messaging.Features;

// docs/10-decisions.md D2: Meta WABA onayı gelene kadar RSVP/opt-out/deterministik intent
// akışları gerçek bir Meta hesabı olmadan uçtan uca test edilebilsin diye - yalnızca
// Development ortamında haritalanır (bkz. MessagingModule.MapMessagingModule).
// Gerçek Meta webhook'unun aksine burada imza doğrulaması yok - zaten dev-only bir kısayol.
public static class DevWhatsAppSimulator
{
    public record SimulateTextRequest(string FromPhoneNumber, string Body);
    public record SimulateRsvpRequest(string FromPhoneNumber, string Action, Guid LessonId);

    public static void MapDevWhatsAppSimulator(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dev/whatsapp").AllowAnonymous();
        group.MapPost("/simulate-text", SimulateTextAsync);
        group.MapPost("/simulate-rsvp", SimulateRsvpAsync);
    }

    private static async Task<IResult> SimulateTextAsync(
        SimulateTextRequest request, AbderaDbContext db, IClock clock, IConfiguration config,
        INotificationScheduler scheduler, IWhatsAppClient whatsAppClient)
    {
        var normalizedFrom = PhoneNumberNormalizer.Normalize(request.FromPhoneNumber);
        var rawBody = BuildMetaTextPayload(normalizedFrom, request.Body);
        await Webhooks.ProcessPayloadAsync(rawBody, db, clock, config, scheduler, whatsAppClient);
        return Results.Ok();
    }

    private static async Task<IResult> SimulateRsvpAsync(
        SimulateRsvpRequest request, AbderaDbContext db, IClock clock, IConfiguration config,
        INotificationScheduler scheduler, IWhatsAppClient whatsAppClient)
    {
        var signingKey = config["WhatsApp:PayloadSigningKey"] ?? "";
        var normalizedFrom = PhoneNumberNormalizer.Normalize(request.FromPhoneNumber);
        var payload = RsvpButtonPayload.Sign(request.Action, request.LessonId, signingKey);
        var buttonText = request.Action == RsvpButtonPayload.AttendingAction ? "✅ Geliyorum" : "❌ Gelemiyorum";
        var rawBody = BuildMetaButtonPayload(normalizedFrom, payload, buttonText);
        await Webhooks.ProcessPayloadAsync(rawBody, db, clock, config, scheduler, whatsAppClient);
        return Results.Ok();
    }

    private static string BuildMetaTextPayload(string fromPhoneNumber, string body) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            entry = new[]
            {
                new
                {
                    changes = new[]
                    {
                        new
                        {
                            value = new
                            {
                                messages = new[]
                                {
                                    new
                                    {
                                        id = $"sim-{Guid.NewGuid()}",
                                        from = fromPhoneNumber,
                                        type = "text",
                                        text = new { body },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        });

    private static string BuildMetaButtonPayload(string fromPhoneNumber, string buttonPayload, string buttonText) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            entry = new[]
            {
                new
                {
                    changes = new[]
                    {
                        new
                        {
                            value = new
                            {
                                messages = new[]
                                {
                                    new
                                    {
                                        id = $"sim-{Guid.NewGuid()}",
                                        from = fromPhoneNumber,
                                        type = "button",
                                        button = new { payload = buttonPayload, text = buttonText },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        });
}
