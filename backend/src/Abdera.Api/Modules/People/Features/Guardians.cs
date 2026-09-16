using System.Security.Claims;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// docs/07-api.md /api/guardians. docs/04-permissions.md: veli bilgisi (telefon, KVKK
// kapsamındaki veri) yalnızca Admin'e açık - öğretmenin veli iletişim bilgisine erişimi yok.
public static class Guardians
{
    public record CreateRequest(string FirstName, string LastName, string PhoneNumber);
    public record UpdateRequest(string FirstName, string LastName, string PhoneNumber);
    public record GuardianResponse(Guid Id, string FirstName, string LastName, string PhoneNumber, bool NotificationConsent);
    // Şifre düz metni yalnızca bu yanıtta bir kez döner (öğretmen TemporaryPassword desenيyle
    // aynı) - admin ekranı gösterebilsin, agent doğrulama için kullanabilsin.
    public record ResetPasswordResponse(Guid Id, string PhoneNumber, string Password, string Message);

    private const string PasswordTemplateName = "guardian_password";

    public static void MapGuardians(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/guardians").RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        // Tüm velilerin listesi Admin'de kalır - öğretmenin okul geneli veli rehberine
        // ihtiyacı yok. Oluşturma ve düzenleme öğretmene açıldı (J1): veli olmadan
        // öğrenciye hiçbir WhatsApp bildirimi gitmiyor, bu yüzden öğrenciyi ekleyen
        // kişinin velisini de girebilmesi gerekiyor.
        group.MapGet("", ListAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPost("", CreateAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        group.MapPatch("/{guardianId:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        group.MapPost("/{guardianId:guid}/reset-password", ResetPasswordAsync);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var guardians = await db.Guardians
            .OrderBy(g => g.LastName).ThenBy(g => g.FirstName)
            .Select(g => new GuardianResponse(g.Id, g.FirstName, g.LastName, g.PhoneNumber, g.NotificationConsent))
            .ToListAsync();

        return Results.Ok(guardians);
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, AbderaDbContext db, IClock clock)
    {
        var normalizedPhone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
        if (await db.Guardians.AnyAsync(g => g.PhoneNumber == normalizedPhone))
        {
            throw new ConflictException("Bu telefon numarasıyla kayıtlı bir veli zaten var.");
        }

        var guardian = Guardian.Create(request.FirstName, request.LastName, request.PhoneNumber, clock.UtcNow);
        db.Guardians.Add(guardian);
        await db.SaveChangesAsync();

        return Results.Created($"/api/guardians/{guardian.Id}",
            new GuardianResponse(guardian.Id, guardian.FirstName, guardian.LastName, guardian.PhoneNumber, guardian.NotificationConsent));
    }

    private static async Task<IResult> UpdateAsync(
        Guid guardianId, UpdateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        // Öğretmen yalnızca kendi öğrencisine bağlı veliyi düzenleyebilir (J1).
        await PeopleAuthorization.EnsureGuardianAccessAsync(guardianId, principal, db);

        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.Id == guardianId)
            ?? throw new NotFoundException("Veli bulunamadı.");

        var normalizedPhone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
        if (normalizedPhone != guardian.PhoneNumber && await db.Guardians.AnyAsync(g => g.PhoneNumber == normalizedPhone))
        {
            throw new ConflictException("Bu telefon numarasıyla kayıtlı başka bir veli var.");
        }

        guardian.Update(request.FirstName, request.LastName, request.PhoneNumber, clock.UtcNow);
        await db.SaveChangesAsync();

        return Results.Ok(new GuardianResponse(guardian.Id, guardian.FirstName, guardian.LastName, guardian.PhoneNumber, guardian.NotificationConsent));
    }

    // Karar F (ikinci) reversal: veliye telefon+şifre girişi için mnemonik bir ilk şifre üretir,
    // hash'ler ve WhatsApp'tan gönderir. Şifre deseni birincil bağlı öğrencinin adı + veli adı +
    // telefon son 4 hanesinden türer (GuardianPasswordGenerator). Düz metin yalnızca yanıtta
    // bir kez döner - loglanmaz.
    private static async Task<IResult> ResetPasswordAsync(
        Guid guardianId, AbderaDbContext db, IClock clock,
        IPasswordHasher<Guardian> passwordHasher, IWhatsAppClient whatsAppClient)
    {
        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.Id == guardianId)
            ?? throw new NotFoundException("Veli bulunamadı.");

        // Birincil bağlı öğrencinin adını al; yoksa herhangi bir bağlı öğrenci, o da yoksa veli
        // adının kendisi. OrderBy anonim ara tip üzerinde, skaler projeksiyondan önce (CLAUDE.md).
        var childFirstName = await db.StudentGuardians
            .Where(sg => sg.GuardianId == guardianId)
            .Join(db.Students, sg => sg.StudentId, s => s.Id, (sg, s) => new { sg.IsPrimary, s.FirstName })
            .OrderByDescending(x => x.IsPrimary)
            .Select(x => x.FirstName)
            .FirstOrDefaultAsync() ?? guardian.FirstName;

        var password = GuardianPasswordGenerator.Generate(childFirstName, guardian.FirstName, guardian.PhoneNumber);
        guardian.SetPassword(passwordHasher.HashPassword(guardian, password), clock.UtcNow);
        await db.SaveChangesAsync();

        await whatsAppClient.SendTemplateAsync(
            guardian.PhoneNumber, PasswordTemplateName,
            new Dictionary<string, string> { ["password"] = password });

        return Results.Ok(new ResetPasswordResponse(
            guardian.Id, guardian.PhoneNumber, password,
            "Şifre üretildi ve WhatsApp'tan gönderildi."));
    }
}
