using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

// Mesaj Merkezi'nin düzenlenebilir şablon yüzeyi. WhatsApp sağlayıcısına gönderilecek
// işlenmiş metin yine NotificationDispatcher'da üretilir; bu uçlar yalnızca admin'in
// onaylı gövdeyi ve placeholder listesini yönetmesine izin verir.
public static class MessageTemplates
{
    public record TemplateResponse(Guid Id, string Name, string Language, string Body, bool IsActive);
    public record CreateRequest(string Name, string Body, string? Language);
    public record UpdateRequest(string Name, string Body, string? Language, bool IsActive);

    public static void MapMessageTemplates(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/message-templates").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPatch("/{templateId:guid}", UpdateAsync);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var templates = await db.MessageTemplates
            .OrderBy(t => t.Name)
            .Select(t => ToResponse(t))
            .ToListAsync();
        return Results.Ok(templates);
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, AbderaDbContext db)
    {
        Validate(request.Name, request.Body);
        if (await db.MessageTemplates.AnyAsync(t => t.Name == request.Name.Trim()))
            throw new ConflictException("Bu adla kayıtlı bir mesaj şablonu zaten var.");

        var template = MessageTemplate.Create(request.Name, request.Body, request.Language ?? "tr");
        db.MessageTemplates.Add(template);
        await db.SaveChangesAsync();
        return Results.Created($"/api/message-templates/{template.Id}", ToResponse(template));
    }

    private static async Task<IResult> UpdateAsync(Guid templateId, UpdateRequest request, AbderaDbContext db)
    {
        Validate(request.Name, request.Body);
        var template = await db.MessageTemplates.SingleOrDefaultAsync(t => t.Id == templateId)
            ?? throw new NotFoundException("Mesaj şablonu bulunamadı.");

        // Name, NotificationMessageBuilder'ın sabit template anahtarıdır; gövde ve aktiflik
        // yönetilebilir ama anahtarın değiştirilmesi otomatik gönderimleri koparmamalıdır.
        template.Update(template.Name, request.Body, request.Language ?? "tr");
        template.SetActive(request.IsActive);
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(template));
    }

    private static void Validate(string name, string body)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Şablon adı boş olamaz."];
        if (name?.Trim().Length > 100) errors["name"] = ["Şablon adı 100 karakterden uzun olamaz."];
        if (string.IsNullOrWhiteSpace(body)) errors["body"] = ["Mesaj gövdesi boş olamaz."];
        if (errors.Count > 0) throw new ValidationFailedException(errors);
    }

    private static TemplateResponse ToResponse(MessageTemplate template) =>
        new(template.Id, template.Name, template.Language, template.Body, template.IsActive);
}
