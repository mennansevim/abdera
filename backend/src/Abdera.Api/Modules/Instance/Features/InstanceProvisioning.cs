using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.Instance.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Instance.Features;

// Yeni okulun ilk kurulumu (docs/multi-tenant/ARCHITECTURE.md §5.4, §6 adım 9). Yalnızca platform
// çağırır (Provisioning:Token). Tek transaction'da: ilk yönetici, okul profili (+logo), okul kuralları,
// aidat politikası ve ilk tarifeler.
//
// İdempotent ve tek kullanımlık:
// - Aynı istek tekrar gelirse (platform cevabı kaybetti) hiçbir şey değişmez, 200 döner.
// - Kurulumdan sonra FARKLI bir istek 409 alır; kurulum kalıcı olarak kapanmıştır.
// - Kullanıcısı olan bir okul (bugünkü canlı okul dahil) hiçbir zaman "kurulamaz": 409.
// Geçici parolayı platform üretir ve istekte gönderir; cevap kaybolsa da platform parolayı bilir.
public static class InstanceProvisioning
{
    public const string ProvisionedAction = "instance.provisioned";
    public const int MinTemporaryPasswordLength = 12;
    // CompleteProgressModule tohumundaki müziğe özel genel beceriler (Teknik ve Çalışma Disiplini her alana uyar).
    private static readonly string[] MusicOnlySkills = ["RHYTHM", "TEMPO_CONTROL", "SIGHT_READING", "MUSICAL_EXPRESSION"];

    public record AdminRequest(string Email, string TemporaryPassword, string? DisplayName);
    public record ProfileRequest(string Name, string ShortName, string? PrimaryColor, string? LogoBase64, string? LegalName = null, string? ContactEmail = null);
    public record PolicyRequest(
        decimal GroupLessonSharePercent, int MaxWeeklyLessons, int MakeupNoticeHours, int MakeupCreditValidDays,
        int ExcusedMakeupWindowDays, TimeOnly SchoolDayStart, TimeOnly SchoolDayEnd);
    public record BillingRequest(decimal MultiCourseDiscountPercent, decimal SiblingDiscountPercent, int DueDayOfMonth);
    public record TariffRequest(CourseKind CourseKind, int LessonsPerMonth, decimal MonthlyAmount, string? Currency, DateOnly EffectiveFrom);
    // Alt birim (branş): kategori BranchCatalog anahtarı, ad okulun kendi verdiği ad ("Bale", "Robotik kodlama").
    public record BranchRequest(string Category, string Name);
    // Okulun kendi açtığı alan (katalogda olmayan); anahtar "c-" önekli, platform sihirbazı üretir.
    public record CategoryRequest(string Key, string Name, string? UnitLabel, string? UnitLabelPlural);
    public record ProvisionRequest(
        AdminRequest Admin, ProfileRequest Profile, PolicyRequest? Policy, BillingRequest? Billing, IReadOnlyList<TariffRequest>? Tariffs,
        IReadOnlyList<BranchRequest>? Branches = null, IReadOnlyList<CategoryRequest>? Categories = null);
    public record ProvisionResponse(bool AlreadyProvisioned, string AdminEmail, DateTimeOffset ProvisionedAt);

    public static void MapInstanceProvisioning(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/instance/provision", ProvisionAsync).AllowAnonymous().RequireRateLimiting("webhooks");
    }

    private static async Task<IResult> ProvisionAsync(
        HttpContext http, ProvisionRequest request, AbderaDbContext db, IConfiguration config,
        IPasswordHasher<User> passwordHasher, IClock clock)
    {
        if (PlatformAccess.Check(http, config, PlatformAccess.ProvisioningTokenKey) is { } denied) return denied;

        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var previous = await db.AuditLogs.AsNoTracking()
            .Where(log => log.Action == ProvisionedAction)
            .OrderBy(log => log.CreatedAt)
            .FirstOrDefaultAsync();
        if (previous is not null)
        {
            var recorded = JsonSerializer.Deserialize<JsonElement>(previous.AfterJson ?? "{}");
            var sameRequest = recorded.TryGetProperty("requestHash", out var hash) && hash.GetString() == requestHash;
            if (!sameRequest) throw new ConflictException("Bu okul zaten kurulmuş; kurulum ucu kapalı.");
            return Results.Ok(new ProvisionResponse(true, recorded.GetProperty("adminEmail").GetString()!, previous.CreatedAt));
        }

        if (await db.Users.AnyAsync())
            throw new ConflictException("Okulda zaten kullanıcı var; kurulum yalnızca boş bir okulda yapılabilir.");

        Validate(request);
        var now = clock.UtcNow;
        // Tohum tarifeleri kaldırmak ara bir SaveChanges gerektiriyor; kurulum yine tek transaction.
        await using var transaction = await db.Database.BeginTransactionAsync();

        var email = request.Admin.Email.Trim().ToLowerInvariant();
        var admin = User.Create(email, "placeholder", UserRole.Admin, now, mustChangePassword: true);
        admin.SetPassword(passwordHasher.HashPassword(admin, request.Admin.TemporaryPassword), now, mustChangePassword: true);
        if (!string.IsNullOrWhiteSpace(request.Admin.DisplayName)) admin.SetDisplayName(request.Admin.DisplayName, now);
        db.Users.Add(admin);

        var profile = SchoolProfile.CreateDefault(now);
        profile.UpdateIdentity(request.Profile.Name, request.Profile.ShortName, request.Profile.PrimaryColor, admin.Id, now);
        profile.UpdateContact(request.Profile.LegalName, request.Profile.ContactEmail, admin.Id, now);
        if (!string.IsNullOrWhiteSpace(request.Profile.LogoBase64))
        {
            byte[] logo;
            try { logo = Convert.FromBase64String(request.Profile.LogoBase64); }
            catch (FormatException) { throw Invalid("profile.logoBase64", "Logo base64 biçiminde olmalı."); }
            if (logo.Length > SchoolProfile.MaxLogoBytes) throw Invalid("profile.logoBase64", "Logo en fazla 512 KB olabilir.");
            profile.ReplaceLogo(logo, admin.Id, now);
        }
        db.SchoolProfiles.Add(profile);

        if (request.Policy is { } policyRequest)
        {
            var policy = SchoolPolicy.CreateDefault(config, now);
            policy.Update(
                policyRequest.GroupLessonSharePercent, policyRequest.MaxWeeklyLessons, policyRequest.MakeupNoticeHours,
                policyRequest.MakeupCreditValidDays, policyRequest.ExcusedMakeupWindowDays,
                policyRequest.SchoolDayStart, policyRequest.SchoolDayEnd, admin.Id, now);
            db.SchoolPolicies.Add(policy);
        }

        if (request.Billing is { } billingRequest)
        {
            var settings = await db.BillingSettings.SingleOrDefaultAsync(s => s.Id == BillingSettings.SingletonId);
            if (settings is null)
            {
                settings = BillingSettings.CreateDefault(now);
                db.BillingSettings.Add(settings);
            }
            settings.Update(billingRequest.MultiCourseDiscountPercent, billingRequest.SiblingDiscountPercent,
                billingRequest.DueDayOfMonth, settings.CycleBillingStartsOn, admin.Id, now);
        }

        // Migration'lar Abdera'nın kendi tarifelerini tohum olarak ekler (RedesignTuitionAndDues). Yeni
        // okul kendi tarifesini getirdiyse ve henüz hiç aidat açılmadıysa bu tohum satırlar kaldırılır;
        // aksi halde okulun ilk tarifesi Abdera'nın fiyatıyla çakışırdı. Aidata bağlı tarife asla silinmez.
        if (request.Tariffs is { Count: > 0 } && !await db.Receivables.AnyAsync())
        {
            db.TuitionRates.RemoveRange(await db.TuitionRates.Where(rate => rate.CreatedBy == null).ToListAsync());
            await db.SaveChangesAsync();
        }

        // Migration'lar Abdera'nın müzik enstrümanlarını tohum olarak ekler (SeedInstruments). Okul kendi
        // alt birimlerini getirdiyse (dans okulu "Piyano" ile başlamasın) ve henüz hiçbir kayıt/öğretmen
        // onlara bağlanmadıysa tohum satırlar kaldırılır, okulun listesi eklenir.
        if (request.Branches is { Count: > 0 } branches)
        {
            if (!await db.Enrollments.AnyAsync() && !await db.TeacherInstruments.AnyAsync() && !await db.LessonGroups.AnyAsync())
            {
                // Enstrümana bağlı tohumlar da müziğe özel: gelişim becerileri, enstrüman bakım ayarları.
                await db.SkillDefinitions.Where(skill => skill.InstrumentId != null).ExecuteDeleteAsync();
                // Genel becerilerin bir kısmı da müziğe özel; müzik öğretmeyen okulda yalnız alan bağımsızları kalır.
                if (!branches.Any(branch => string.Equals(branch.Category, Shared.Branches.BranchCatalog.Music, StringComparison.OrdinalIgnoreCase)))
                    await db.SkillDefinitions.Where(skill => MusicOnlySkills.Contains(skill.Code)).ExecuteDeleteAsync();
                await db.InstrumentMaintenanceSettings.ExecuteDeleteAsync();
                await db.GrowthIdeas.Where(idea => idea.InstrumentId != null).ExecuteDeleteAsync();
                await db.Instruments.ExecuteDeleteAsync();
            }
            foreach (var category in request.Categories ?? [])
                if (!await db.CustomBranchCategories.AnyAsync(c => c.Key == category.Key))
                    await People.Features.BranchCategories.AddAsync(db, category.Key, category.Name, category.UnitLabel, category.UnitLabelPlural, now);
            foreach (var branch in branches)
                if (!await db.Instruments.AnyAsync(i => i.Name == branch.Name.Trim()))
                    await People.Features.Instruments.AddAsync(db, branch.Name, null, branch.Category);
        }

        foreach (var tariff in (request.Tariffs ?? []).OrderBy(tariff => tariff.EffectiveFrom))
        {
            await TuitionRates.AddRateAsync(
                new TuitionRates.CreateRequest(tariff.CourseKind, tariff.LessonsPerMonth, tariff.MonthlyAmount, tariff.EffectiveFrom, tariff.Currency),
                admin.Id, db, now);
        }

        // Kaydın kendisi tek kullanımlık kilit: aynı istek tanınır, farklı istek reddedilir.
        // Kişisel veri olarak yalnızca yöneticinin e-postası yazılır; parola ve logo yazılmaz.
        db.AuditLogs.Add(AuditLog.Record(
            null, ProvisionedAction, nameof(SchoolProfile), profile.Id, now,
            afterJson: JsonSerializer.Serialize(new
            {
                requestHash,
                adminEmail = email,
                schoolName = profile.Name,
                tariffCount = request.Tariffs?.Count ?? 0,
                branchCount = request.Branches?.Count ?? 0,
                customCategoryCount = request.Categories?.Count ?? 0,
                policy = request.Policy is not null,
            })));

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Ok(new ProvisionResponse(false, email, now));
    }

    private static void Validate(ProvisionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var email = request.Admin?.Email?.Trim() ?? string.Empty;
        if (email.Length is 0 or > 254 || !email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
            errors["admin.email"] = ["Geçerli bir yönetici e-postası girilmeli."];
        if ((request.Admin?.TemporaryPassword?.Length ?? 0) < MinTemporaryPasswordLength)
            errors["admin.temporaryPassword"] = [$"Geçici parola en az {MinTemporaryPasswordLength} karakter olmalı."];
        foreach (var (tariff, index) in (request.Tariffs ?? []).Select((tariff, index) => (tariff, index)))
        {
            if (tariff.MonthlyAmount < 0) errors[$"tariffs[{index}].monthlyAmount"] = ["Tutar negatif olamaz."];
            if (tariff.LessonsPerMonth is < 1 or > 31) errors[$"tariffs[{index}].lessonsPerMonth"] = ["Aylık ders sayısı 1 ile 31 arasında olmalı."];
        }
        var customKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, index) in (request.Categories ?? []).Select((category, index) => (category, index)))
        {
            if (!Shared.Branches.BranchCatalog.IsCustomKey(category.Key?.ToLowerInvariant())) errors[$"categories[{index}].key"] = ["Alan anahtarı 'c-' ile başlamalı."];
            else if (!customKeys.Add(category.Key)) errors[$"categories[{index}].key"] = ["Aynı alan iki kez verilmiş."];
            if (string.IsNullOrWhiteSpace(category.Name) || category.Name.Trim().Length > 60) errors[$"categories[{index}].name"] = ["Alan adı 1-60 karakter olmalı."];
        }
        foreach (var (branch, index) in (request.Branches ?? []).Select((branch, index) => (branch, index)))
        {
            if (Shared.Branches.BranchCatalog.Find(branch.Category) is null && !customKeys.Contains(branch.Category ?? ""))
                errors[$"branches[{index}].category"] = ["Bilinmeyen kategori."];
            if (string.IsNullOrWhiteSpace(branch.Name) || branch.Name.Trim().Length > 100) errors[$"branches[{index}].name"] = ["Ad 1-100 karakter olmalı."];
        }
        if ((request.Branches ?? []).GroupBy(b => (b.Name ?? "").Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            errors["branches"] = ["Aynı ad iki kez verilmiş."];
        if (errors.Count > 0) throw new ValidationFailedException(errors);
    }

    private static ValidationFailedException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
