using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
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
    // NotificationConsent: künyedeki "WhatsApp bildirimi alsın" kutusu (varsayılan kapalı).
    // Students.UpdateRequest.SiblingDiscount ile aynı sözleşme: gönderilmezse alana dokunulmaz,
    // Admin değilse yok sayılır - öğretmenin iletişim bilgisi düzenlemesi onayı değiştirmesin.
    public record UpdateRequest(string FirstName, string LastName, string PhoneNumber, bool? NotificationConsent = null);
    public record GuardianResponse(Guid Id, string FirstName, string LastName, string PhoneNumber, bool NotificationConsent);
    public record PhoneLookupResponse(Guid Id, string FirstName, string LastName, string PhoneNumber, List<string> StudentNames);
    // Şifre düz metni yalnızca bu yanıtta bir kez döner (öğretmen TemporaryPassword desenيyle
    // aynı) - admin ekranı gösterebilsin, agent doğrulama için kullanabilsin.
    public record ResetPasswordResponse(Guid Id, string PhoneNumber, string Password, string Message);

    public static void MapGuardians(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/guardians").RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        // Tüm velilerin listesi Admin'de kalır - öğretmenin okul geneli veli rehberine
        // ihtiyacı yok. Oluşturma ve düzenleme öğretmene açıldı (J1): veli olmadan
        // öğrenciye hiçbir WhatsApp bildirimi gitmiyor, bu yüzden öğrenciyi ekleyen
        // kişinin velisini de girebilmesi gerekiyor.
        group.MapGet("", ListAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPost("", CreateAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        // "Bu numara zaten kayıtlı" çakışmasında yöneticinin mevcut veliyi (kardeşin velisi)
        // yeni öğrenciye bağlayabilmesi için. Hangi öğrencinin velisi olduğunu da söylediği
        // için yalnızca Admin'e açık - öğretmen başka öğretmenin öğrencisini öğrenmemeli.
        group.MapGet("/by-phone", LookupByPhoneAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPatch("/{guardianId:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        // Veli şifresi sıfırlama hesap devralmaya yol açabilecek bir yönetim işlemidir.
        // Öğretmen kendi öğrencisinin iletişim bilgisini güncelleyebilir ama oturum
        // bilgisini değiştiremez; bu sınır yalnızca arayüzde değil API'de zorlanır.
        group.MapPost("/{guardianId:guid}/reset-password", ResetPasswordAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
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
        var existing = await db.Guardians.SingleOrDefaultAsync(g => g.PhoneNumber == normalizedPhone);
        if (existing is not null)
        {
            if (await db.StudentGuardians.AnyAsync(link => link.GuardianId == existing.Id))
            {
                throw new ConflictException("Bu telefon numarasıyla kayıtlı bir veli zaten var.");
            }

            // Hiçbir öğrenciye bağlı olmayan veli: öğrencisi silinmiş (eski silme akışı veliyi
            // bırakıyordu) ya da bağlanmadan yarım kalmış bir kayıt. Aynı numara aynı kişidir;
            // yeni bir satır açılamayacağı (UNIQUE phone_number) için bu kayıt yeniden kullanılır.
            existing.Update(request.FirstName, request.LastName, request.PhoneNumber, clock.UtcNow);
            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(existing));
        }

        var guardian = Guardian.Create(request.FirstName, request.LastName, request.PhoneNumber, clock.UtcNow);
        db.Guardians.Add(guardian);
        await db.SaveChangesAsync();

        return Results.Created($"/api/guardians/{guardian.Id}", ToResponse(guardian));
    }

    private static async Task<IResult> LookupByPhoneAsync(string phone, AbderaDbContext db)
    {
        string normalizedPhone;
        try
        {
            normalizedPhone = PhoneNumberNormalizer.Normalize(phone);
        }
        catch (ArgumentException)
        {
            throw new NotFoundException("Bu numarayla kayıtlı veli yok.");
        }

        var guardian = await db.Guardians.AsNoTracking().SingleOrDefaultAsync(g => g.PhoneNumber == normalizedPhone)
            ?? throw new NotFoundException("Bu numarayla kayıtlı veli yok.");

        var studentNames = await db.StudentGuardians
            .Where(link => link.GuardianId == guardian.Id)
            .Join(db.Students, link => link.StudentId, student => student.Id, (link, student) => new { student.FirstName, student.LastName })
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Select(x => x.FirstName + " " + x.LastName)
            .ToListAsync();

        return Results.Ok(new PhoneLookupResponse(guardian.Id, guardian.FirstName, guardian.LastName, guardian.PhoneNumber, studentNames));
    }

    private static GuardianResponse ToResponse(Guardian guardian) =>
        new(guardian.Id, guardian.FirstName, guardian.LastName, guardian.PhoneNumber, guardian.NotificationConsent);

    private static async Task<IResult> UpdateAsync(
        Guid guardianId, UpdateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock,
        INotificationScheduler scheduler)
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

        var previousPhone = guardian.PhoneNumber;
        guardian.Update(request.FirstName, request.LastName, request.PhoneNumber, clock.UtcNow);

        if (request.NotificationConsent is { } consent && consent != guardian.NotificationConsent &&
            AuthContext.IsAdmin(principal))
        {
            var now = clock.UtcNow;
            guardian.SetNotificationConsent(consent, now);
            if (consent)
            {
                await scheduler.ScheduleUpcomingLessonRemindersAsync(guardian.Id);
            }
            else
            {
                // Bekleyen job'lar eski numaraya kurulmuş olabilir (numara aynı istekte değiştiyse).
                await scheduler.CancelPendingForRecipientAsync(previousPhone);
                await scheduler.CancelPendingForRecipientAsync(guardian.PhoneNumber);
            }

            // CLAUDE.md: rıza (consent) değiştiren her use-case audit_log'a yazar.
            db.AuditLogs.Add(AuditLog.Record(
                AuthContext.GetUserId(principal), "guardian.notification_consent_changed", nameof(Guardian), guardian.Id, now,
                beforeJson: JsonSerializer.Serialize(new { notificationConsent = !consent }),
                afterJson: JsonSerializer.Serialize(new { notificationConsent = consent })));
        }

        await db.SaveChangesAsync();

        return Results.Ok(ToResponse(guardian));
    }

    // Veli şifresini ad soyaddan türeyen varsayılana döndürür (docs/10-decisions.md Q1,
    // GuardianPasswordGenerator), hash'ler ve WhatsApp'tan gönderir. Veli kendi şifresini
    // değiştirip unuttuğunda yöneticinin geri dönüş yolu. Düz metin yalnızca yanıtta bir kez
    // döner - loglanmaz.
    //
    // R2: yeni veliye ilk gönderim onay kapalıyken de gider (tek seferlik karşılama mesajı);
    // istisnayı ConsentGatedWhatsAppClient uygular, burada yalnızca borç kapatılır ve onaysız
    // gönderim audit_log'a yazılır.
    private static async Task<IResult> ResetPasswordAsync(
        Guid guardianId, ClaimsPrincipal principal, AbderaDbContext db, IClock clock,
        IPasswordHasher<Guardian> passwordHasher, IWhatsAppClient whatsAppClient)
    {
        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.Id == guardianId)
            ?? throw new NotFoundException("Veli bulunamadı.");

        var password = GuardianPasswordGenerator.Generate(guardian.FirstName, guardian.LastName);
        guardian.SetPassword(passwordHasher.HashPassword(guardian, password), clock.UtcNow);
        await db.SaveChangesAsync();

        var sendResult = await whatsAppClient.SendTemplateAsync(
            guardian.PhoneNumber, WhatsAppTemplateNames.GuardianPassword,
            new Dictionary<string, string> { ["password"] = password });

        var sentAsWelcome = sendResult.Success && guardian.WelcomeMessagePending;
        if (sentAsWelcome)
        {
            var now = clock.UtcNow;
            guardian.MarkWelcomeMessageDelivered(now);
            if (!guardian.NotificationConsent)
            {
                db.AuditLogs.Add(AuditLog.Record(
                    AuthContext.GetUserId(principal), "guardian.welcome_message_sent", nameof(Guardian), guardian.Id, now,
                    afterJson: JsonSerializer.Serialize(new { notificationConsent = false, template = WhatsAppTemplateNames.GuardianPassword })));
            }
            await db.SaveChangesAsync();
        }

        // Gönderim başarısızsa (ör. bildirim onayı kapalı) "gönderildi" deme - yönetici şifreyi
        // bu yanıttan görüp veliye kendisi iletir.
        return Results.Ok(new ResetPasswordResponse(
            guardian.Id, guardian.PhoneNumber, password,
            sendResult.Success
                ? sentAsWelcome && !guardian.NotificationConsent
                    ? "Giriş bilgileri WhatsApp'tan gönderildi. Bildirim onayı kapalı olduğu için sonraki mesajlar gitmez."
                    : "Şifre üretildi ve WhatsApp'tan gönderildi."
                : $"Şifre üretildi ama WhatsApp'tan gönderilemedi ({sendResult.Error}). Veliye kendin ilet."));
    }
}
