using System.Security.Claims;
using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Persistence;
using Abdera.Platform.Api.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Features;

// Okullar: kutucuk listesi, sihirbaz (§4.2), detay (§4.3) ve okul işlemleri.
// Hiçbir cevap sır taşımaz; yöneticinin geçici parolası yalnızca açıkça istenince döner ve audit'e yazılır.
public static class Schools
{
    // --- Sihirbaz isteği: okul uygulamasının kurulum sözleşmesine (InstanceProvisioning) dönüşür ---
    public record AdminRequest(string Email, string? DisplayName);
    public record PolicyRequest(
        decimal GroupLessonSharePercent, int MaxWeeklyLessons, int MakeupNoticeHours, int MakeupCreditValidDays,
        int ExcusedMakeupWindowDays, TimeOnly SchoolDayStart, TimeOnly SchoolDayEnd);
    public record BillingRequest(decimal MultiCourseDiscountPercent, decimal SiblingDiscountPercent, int DueDayOfMonth);
    public record TariffRequest(string CourseKind, int LessonsPerMonth, decimal MonthlyAmount, string? Currency, DateOnly EffectiveFrom);
    public record CreateRequest(
        string Slug, string DisplayName, string ShortName, string? PrimaryColor, string? LogoBase64,
        string? LegalName, string? ContactEmail, string? CustomDomain, string Version, IReadOnlyList<string> Modules,
        AdminRequest Admin, PolicyRequest? Policy, BillingRequest? Billing, IReadOnlyList<TariffRequest>? Tariffs,
        IReadOnlyDictionary<string, string>? Integrations, bool IsCanary, IReadOnlyList<BranchRequest>? Branches = null,
        IReadOnlyList<CustomCategoryRequest>? CustomCategories = null, PackageRequest? Package = null);
    // Kullanıcı paketi (SchoolPackage): personel hesabı sınırı, kullanıcı başı fiyat ve seçilen eklentilerin
    // yıllık fiyatı. Sihirbazdan kurulan okulda zorunlu; sahiplenmede boşsa okul sınırsız kalır.
    public record PackageRequest(int? StaffSeats, decimal? PricePerSeat, IReadOnlyDictionary<string, decimal>? AddonPrices);
    public record PackageResponse(int? StaffSeats, decimal? PricePerSeat, IReadOnlyDictionary<string, decimal> AddonPrices, string Currency, decimal YearlyTotal);
    // Okulun alt birimi (branş): kategori sürümün kataloğundan (Releases.CategoryEntry) ya da okulun
    // kendi açtığı alandan (CustomCategories), ad serbest.
    public record BranchRequest(string Category, string Name);
    // Katalogda olmayan alan: anahtar "c-" önekli (okul uygulaması BranchCatalog.IsCustomKey ile aynı kural).
    public record CustomCategoryRequest(string Key, string Name, string? UnitLabel, string? UnitLabelPlural);
    public record CategoryName(string Key, string Name);

    public record TelemetryResponse(
        DateTimeOffset TakenAt, bool Reachable, string? Health, string? HealthDetail, string? AppVersion, int? ActiveStudents,
        int? ActiveTeachers, int? ActiveEnrollments, DateTimeOffset? LastBackupAt, long? DatabaseSizeBytes, int? PendingNotifications,
        int? FailedNotifications, string? Error, int? ActiveStaffUsers);
    public record TileResponse(
        Guid Id, string Slug, string DisplayName, string ShortName, string? PrimaryColor, SchoolStatus Status, string PrimaryDomain,
        string? AppVersion, string? TargetVersion, string? HostName, bool IsCanary, bool IsVersionPinned, TelemetryResponse? Telemetry,
        RunSummary? OpenRun, int OpenAlerts, IReadOnlyList<string> Categories, IReadOnlyList<CategoryName> CustomCategories,
        string PublicDomain, PackageResponse Package);
    public record RunSummary(Guid Id, RunKind Kind, RunStatus Status, int CurrentStep, int TotalSteps, string? CurrentStepName, string? Error, DateTimeOffset CreatedAt);
    public record StepResponse(int Step, string Name, StepStatus Status, int Attempt, string? Log, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);
    public record RunResponse(Guid Id, RunKind Kind, RunStatus Status, int CurrentStep, IReadOnlyList<string> Plan, string? TargetVersion, string? Error,
        DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, IReadOnlyList<StepResponse> Steps);
    public record AlertResponse(Guid Id, string Kind, string Level, string Detail, DateTimeOffset OccurredAt, DateTimeOffset? AcknowledgedAt);
    public record DetailResponse(TileResponse School, IReadOnlyList<string> Modules, string? AdminEmail, IReadOnlyList<string> SecretNames,
        IReadOnlyList<RunResponse> Runs, IReadOnlyList<TelemetryResponse> TelemetryHistory, IReadOnlyList<AlertResponse> Alerts, DateTimeOffset CreatedAt,
        SchoolDomains.DomainResponse? Domain = null);
    // Sınır değişikliği okulun .env'ini yeniden yazar (Reconfigure); yalnızca fiyat değişikliği okula gitmez.
    public record PackageUpdateRequest(int? StaffSeats, decimal? PricePerSeat, IReadOnlyDictionary<string, decimal>? AddonPrices);
    public record UpgradeRequest(string Version);
    public record FlagsRequest(bool IsCanary, bool IsVersionPinned);
    public record ModulesRequest(IReadOnlyList<string> Modules, IReadOnlyDictionary<string, string>? Integrations);
    public record SlugCheckResponse(bool Available, string? Reason, string Domain);
    public record AdoptRequest(
        string Slug, string DisplayName, string ShortName, string? PrimaryColor, string? CustomDomain, string Version,
        IReadOnlyList<string> Modules, string DumpName, IReadOnlyDictionary<string, string>? Integrations, bool IsCanary,
        PackageRequest? Package = null);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapSchools(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/schools").RequireAuthorization(Auth.OperatorPolicy);
        group.MapGet("", ListAsync);
        group.MapGet("/check-slug", CheckSlugAsync);
        group.MapPost("", CreateAsync);
        group.MapPost("/adopt", AdoptAsync);
        group.MapGet("/{id:guid}", DetailAsync);
        group.MapPost("/{id:guid}/reveal-admin-password", RevealAdminPasswordAsync);
        group.MapPost("/{id:guid}/retry", RetryAsync);
        group.MapPost("/{id:guid}/cancel", (Guid id, ClaimsPrincipal p, PlatformDbContext db, TimeProvider t) => EnqueueAsync(id, RunKind.Cancel, p, db, t, s =>
            s.Status == SchoolStatus.Failed ? null : "Yalnızca kurulumu başarısız olan okul iptal edilebilir."));
        group.MapPost("/{id:guid}/suspend", (Guid id, ClaimsPrincipal p, PlatformDbContext db, TimeProvider t) => EnqueueAsync(id, RunKind.Suspend, p, db, t, s =>
            s.Status == SchoolStatus.Active ? null : "Yalnızca etkin okul askıya alınabilir."));
        group.MapPost("/{id:guid}/resume", (Guid id, ClaimsPrincipal p, PlatformDbContext db, TimeProvider t) => EnqueueAsync(id, RunKind.Resume, p, db, t, s =>
            s.Status == SchoolStatus.Suspended ? null : "Yalnızca askıdaki okul devam ettirilebilir."));
        group.MapPost("/{id:guid}/destroy", (Guid id, ClaimsPrincipal p, PlatformDbContext db, TimeProvider t) => EnqueueAsync(id, RunKind.Destroy, p, db, t, s =>
            s.Status is SchoolStatus.Active or SchoolStatus.Suspended ? null : "Bu okul silinemez."));
        group.MapPost("/{id:guid}/undestroy", (Guid id, ClaimsPrincipal p, PlatformDbContext db, TimeProvider t) => EnqueueAsync(id, RunKind.Undestroy, p, db, t, s =>
            s.Status == SchoolStatus.PendingDestroy ? null : "Bekleyen bir silme yok."));
        group.MapPost("/{id:guid}/purge", (Guid id, ClaimsPrincipal p, PlatformDbContext db, TimeProvider t) => EnqueueAsync(id, RunKind.Purge, p, db, t, s =>
            s.Status == SchoolStatus.PendingDestroy ? null : "Önce silme başlatılmalı (30 günlük bekleme)."));
        group.MapPost("/{id:guid}/drill", (Guid id, ClaimsPrincipal p, PlatformDbContext db, TimeProvider t) => EnqueueAsync(id, RunKind.Drill, p, db, t, s =>
            s.Status is SchoolStatus.Active or SchoolStatus.Suspended ? null : "Prova yalnızca kurulmuş okulda yapılabilir."));
        group.MapPost("/{id:guid}/upgrade", UpgradeAsync);
        group.MapPut("/{id:guid}/flags", FlagsAsync);
        group.MapPut("/{id:guid}/modules", ModulesAsync);
        group.MapPut("/{id:guid}/package", PackageAsync);
    }

    private static string DomainFor(IConfiguration config, string slug, string? custom) =>
        string.IsNullOrWhiteSpace(custom)
            ? $"{slug}.{config["Dns:BaseDomain"] ?? throw new InvalidOperationException("Dns:BaseDomain tanımlı değil (okul alt alan adlarının tabanı).")}"
            : custom.Trim().ToLowerInvariant();

    private static async Task<IResult> CheckSlugAsync(string slug, PlatformDbContext db, IConfiguration config)
    {
        var domain = DomainFor(config, slug ?? "", null);
        if (!Slug.IsValid(slug)) return Results.Ok(new SlugCheckResponse(false, "3-31 karakter; küçük harf, rakam ve tire.", domain));
        if (Slug.Reserved.Contains(slug!)) return Results.Ok(new SlugCheckResponse(false, "Bu ad ayrılmış.", domain));
        if (await db.Schools.AnyAsync(s => s.Slug == slug)) return Results.Ok(new SlugCheckResponse(false, "Bu kısaltma kullanılıyor.", domain));
        return Results.Ok(new SlugCheckResponse(true, null, domain));
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest request, ClaimsPrincipal principal, PlatformDbContext db, SecretVault vault, IConfiguration config, TimeProvider time)
    {
        var now = time.GetUtcNow();
        var release = await db.Releases.SingleOrDefaultAsync(r => r.Version == request.Version && !r.IsWithdrawn)
            ?? throw ValidationFailedException.For("version", "Sürüm bulunamadı ya da geri çekilmiş.");
        var modules = ValidateModules(request.Modules ?? [], Releases.Catalog(release));
        var customCategories = ValidateCustomCategories(request.CustomCategories ?? [], Releases.Categories(release));
        var branches = ValidateBranches(request.Branches ?? [], Releases.Categories(release), customCategories.Select(c => c.Key).ToList());
        ValidateIntegrations(request.Integrations);
        var email = request.Admin?.Email?.Trim().ToLowerInvariant() ?? "";
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _)) throw ValidationFailedException.For("admin.email", "Geçerli bir yönetici e-postası girilmeli.");
        if (request.LogoBase64 is { Length: > 700_000 }) throw ValidationFailedException.For("logoBase64", "Logo en fazla 512 KB olabilir.");
        if (request.Package?.StaffSeats is null) throw ValidationFailedException.For("package.staffSeats", "Kullanıcı sayısı (personel hesabı) girilmeli.");
        var domain = DomainFor(config, request.Slug, request.CustomDomain);
        if (!System.Text.RegularExpressions.Regex.IsMatch(domain, @"^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$"))
            throw ValidationFailedException.For("customDomain", "Geçerli bir alan adı girilmeli.");
        if (await db.Schools.AnyAsync(s => s.Slug == request.Slug)) throw new ConflictException("Bu kısaltma kullanılıyor.");
        if (await db.Schools.AnyAsync(s => s.PrimaryDomain == domain)) throw new ConflictException("Bu alan adı başka bir okulda.");

        var school = School.Create(request.Slug, request.DisplayName, request.ShortName, domain, release.Version, modules, request.PrimaryColor, now);
        if (request.IsCanary) school.SetCanary(true, now);
        school.SetPackage(request.Package.StaffSeats, request.Package.PricePerSeat, request.Package.AddonPrices ?? new Dictionary<string, decimal>(), now);
        school.SetCategories(branches.Select(branch => branch.Category), now,
            customCategories.Where(c => branches.Any(b => b.Category == c.Key)).ToDictionary(c => c.Key, c => c.Name));
        db.Schools.Add(school);

        // Geçici parolayı platform üretir: okulun kurulum ucu cevabı kaybolsa da parola bilinir.
        var temporaryPassword = SecretVault.TemporaryPassword();
        await vault.SetAsync(school.Id, SecretNames.AdminTemporaryPassword, temporaryPassword);
        await vault.SetAsync(school.Id, SecretNames.ProvisionRequest, JsonSerializer.Serialize(new
        {
            admin = new { email, temporaryPassword, displayName = request.Admin!.DisplayName },
            profile = new
            {
                name = school.DisplayName,
                shortName = school.ShortName,
                primaryColor = request.PrimaryColor,
                logoBase64 = request.LogoBase64,
                legalName = request.LegalName,
                contactEmail = request.ContactEmail,
            },
            policy = request.Policy,
            billing = request.Billing,
            tariffs = request.Tariffs,
            // Boşsa okul migration'ın müzik tohumlarıyla açılır (eski davranış).
            branches = branches.Count > 0 ? branches : null,
            categories = customCategories.Count > 0 ? customCategories : null,
        }, Json));
        foreach (var (key, value) in request.Integrations ?? new Dictionary<string, string>())
            if (!string.IsNullOrWhiteSpace(value)) await vault.SetAsync(school.Id, SecretNames.IntegrationPrefix + key, value.Trim());

        db.Runs.Add(ProvisioningRun.Create(school.Id, RunKind.Create, now, release.Version));
        // Audit'e sır yazılmaz: yalnızca kimlik, sürüm, modüller ve hangi entegrasyonların girildiği.
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "school.created", school.Id, now,
            afterJson: JsonSerializer.Serialize(new
            {
                school.Slug, school.DisplayName, school.PrimaryDomain, release.Version, modules, adminEmail = email, categories = school.Categories,
                package = ToPackage(school),
                integrations = (request.Integrations ?? new Dictionary<string, string>()).Keys.Order().ToList(),
            })));
        await db.SaveChangesAsync();
        return Results.Created($"/api/schools/{school.Id}", new { school.Id });
    }

    // Var olan okulu platforma taşır (PLATFORM.md §9): yeni sunucuda eski okulun dökümü geri yüklenir.
    private static async Task<IResult> AdoptAsync(
        AdoptRequest request, ClaimsPrincipal principal, PlatformDbContext db, SecretVault vault, IConfiguration config, TimeProvider time)
    {
        var now = time.GetUtcNow();
        var release = await db.Releases.SingleOrDefaultAsync(r => r.Version == request.Version && !r.IsWithdrawn)
            ?? throw ValidationFailedException.For("version", "Sürüm bulunamadı ya da geri çekilmiş.");
        var modules = ValidateModules(request.Modules ?? [], Releases.Catalog(release));
        ValidateIntegrations(request.Integrations);
        if (!System.Text.RegularExpressions.Regex.IsMatch(request.DumpName ?? "", @"^[A-Za-z0-9._-]{1,120}\.sql\.gz(\.enc)?$"))
            throw ValidationFailedException.For("dumpName", "Döküm adı '<ad>.sql.gz' biçiminde olmalı.");
        var domain = DomainFor(config, request.Slug, request.CustomDomain);
        if (await db.Schools.AnyAsync(s => s.Slug == request.Slug)) throw new ConflictException("Bu kısaltma kullanılıyor.");
        if (await db.Schools.AnyAsync(s => s.PrimaryDomain == domain)) throw new ConflictException("Bu alan adı başka bir okulda.");

        var school = School.Create(request.Slug, request.DisplayName, request.ShortName, domain, release.Version, modules, request.PrimaryColor, now);
        if (request.IsCanary) school.SetCanary(true, now);
        if (request.Package is { } package) school.SetPackage(package.StaffSeats, package.PricePerSeat, package.AddonPrices ?? new Dictionary<string, decimal>(), now);
        db.Schools.Add(school);
        await vault.SetAsync(school.Id, SecretNames.AdoptDumpName, request.DumpName!);
        foreach (var (key, value) in request.Integrations ?? new Dictionary<string, string>())
            if (!string.IsNullOrWhiteSpace(value)) await vault.SetAsync(school.Id, SecretNames.IntegrationPrefix + key, value.Trim());
        db.Runs.Add(ProvisioningRun.Create(school.Id, RunKind.Adopt, now, release.Version));
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "school.adopt_requested", school.Id, now,
            afterJson: JsonSerializer.Serialize(new { school.Slug, school.PrimaryDomain, release.Version, modules, request.DumpName, package = ToPackage(school) })));
        await db.SaveChangesAsync();
        return Results.Created($"/api/schools/{school.Id}", new { school.Id });
    }

    // Seçilen opsiyonel modüller sürümün kataloğunda olmalı ve bağımlılıkları da seçilmiş olmalı.
    // Kategori sürümün kataloğunda olmalı (kataloğu olmayan eski sürümde yalnızca "other"); ad 1-100
    // karakter ve okul içinde benzersiz. En fazla 200 alt birim.
    private static readonly System.Text.RegularExpressions.Regex CustomKeyPattern = new(@"^c-[a-z0-9]+(-[a-z0-9]+)*$");

    public static List<CustomCategoryRequest> ValidateCustomCategories(IReadOnlyList<CustomCategoryRequest> categories, IReadOnlyList<Releases.CategoryEntry> catalog)
    {
        if (categories.Count > 50) throw ValidationFailedException.For("customCategories", "En fazla 50 özel alan eklenebilir.");
        var result = new List<CustomCategoryRequest>();
        foreach (var (category, index) in categories.Select((category, index) => (category, index)))
        {
            var key = category.Key?.Trim().ToLowerInvariant() ?? "";
            var name = category.Name?.Trim() ?? "";
            if (key.Length > 40 || !CustomKeyPattern.IsMatch(key)) throw ValidationFailedException.For($"customCategories[{index}].key", "Alan anahtarı 'c-' ile başlamalı (küçük harf, rakam, tire).");
            if (name.Length is 0 or > 60) throw ValidationFailedException.For($"customCategories[{index}].name", "Alan adı 1-60 karakter olmalı.");
            if ((category.UnitLabel?.Trim().Length ?? 0) > 40 || (category.UnitLabelPlural?.Trim().Length ?? 0) > 40)
                throw ValidationFailedException.For($"customCategories[{index}].unitLabel", "Terim en fazla 40 karakter olabilir.");
            if (catalog.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) || result.Any(r => r.Key == key || string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw ValidationFailedException.For($"customCategories[{index}].name", $"'{name}' alanı zaten var.");
            result.Add(new CustomCategoryRequest(key, name, category.UnitLabel?.Trim(), category.UnitLabelPlural?.Trim()));
        }
        return result;
    }

    public static List<BranchRequest> ValidateBranches(IReadOnlyList<BranchRequest> branches, IReadOnlyList<Releases.CategoryEntry> catalog, IReadOnlyList<string>? customKeys = null)
    {
        var known = catalog.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (known.Count == 0) known.Add("other");
        known.UnionWith(customKeys ?? []);
        if (branches.Count > 200) throw ValidationFailedException.For("branches", "En fazla 200 alt birim eklenebilir.");
        var result = new List<BranchRequest>();
        foreach (var (branch, index) in branches.Select((branch, index) => (branch, index)))
        {
            var name = branch.Name?.Trim() ?? "";
            if (!known.Contains(branch.Category ?? "")) throw ValidationFailedException.For($"branches[{index}].category", "Bu sürümde olmayan kategori.");
            if (name.Length is 0 or > 100) throw ValidationFailedException.For($"branches[{index}].name", "Ad 1-100 karakter olmalı.");
            if (result.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw ValidationFailedException.For($"branches[{index}].name", $"'{name}' iki kez verilmiş.");
            result.Add(new BranchRequest(branch.Category!.ToLowerInvariant(), name));
        }
        return result;
    }

    public static List<string> ValidateModules(IReadOnlyList<string> selected, IReadOnlyList<Releases.ModuleEntry> catalog)
    {
        var byKey = catalog.ToDictionary(m => m.Key);
        var unknown = selected.Where(key => !byKey.ContainsKey(key)).ToList();
        if (unknown.Count > 0) throw ValidationFailedException.For("modules", $"Bu sürümde olmayan modül: {string.Join(", ", unknown)}");
        // Hedef sürümde standart pakete girmiş (çekirdek) modül seçimden düşer: eski sürümde opsiyonel
        // seçilmiş "progress", "groups"... yeni sürümde zaten açıktır, güncellemeyi engellemez.
        selected = selected.Where(key => !byKey[key].IsCore).ToList();
        var enabled = catalog.Where(m => m.IsCore).Select(m => m.Key).Concat(selected).ToHashSet();
        var missing = selected.SelectMany(key => byKey[key].Requires.Where(r => !enabled.Contains(r)).Select(r => $"{byKey[key].Name} → {byKey.GetValueOrDefault(r)?.Name ?? r}")).ToList();
        if (missing.Count > 0) throw ValidationFailedException.For("modules", $"Bağımlılığı seçilmemiş modül: {string.Join(", ", missing)}");
        return selected.Distinct().ToList();
    }

    private static void ValidateIntegrations(IReadOnlyDictionary<string, string>? integrations)
    {
        var bad = (integrations ?? new Dictionary<string, string>()).Keys.Where(key => !SchoolEnvBuilder.AllowedIntegrationKeys.Contains(key)).ToList();
        if (bad.Count > 0) throw ValidationFailedException.For("integrations", $"İzin verilmeyen ayar: {string.Join(", ", bad)}");
        if ((integrations ?? new Dictionary<string, string>()).Values.Any(value => value.Contains('\n') || value.Length > 2000))
            throw ValidationFailedException.For("integrations", "Ayar değerleri tek satır ve en fazla 2000 karakter olmalı.");
    }

    private static async Task<IResult> ListAsync(PlatformDbContext db, ProvisioningEngine engine)
    {
        var schools = await db.Schools.Where(s => s.Status != SchoolStatus.Destroyed).OrderBy(s => s.DisplayName).ToListAsync();
        return Results.Ok(await TilesAsync(db, engine, schools));
    }

    private static async Task<List<TileResponse>> TilesAsync(PlatformDbContext db, ProvisioningEngine engine, List<School> schools)
    {
        var ids = schools.Select(s => s.Id).ToList();
        var hosts = await db.Hosts.ToDictionaryAsync(h => h.Id, h => h.Name);
        var latest = (await db.Telemetry.Where(t => ids.Contains(t.SchoolId)).GroupBy(t => t.SchoolId)
            .Select(g => g.OrderByDescending(t => t.TakenAt).First()).ToListAsync()).ToDictionary(t => t.SchoolId);
        var openRuns = (await db.Runs.Where(r => ids.Contains(r.SchoolId) && (r.Status == RunStatus.Pending || r.Status == RunStatus.Running || r.Status == RunStatus.Failed))
            .OrderByDescending(r => r.CreatedAt).ToListAsync()).GroupBy(r => r.SchoolId).ToDictionary(g => g.Key, g => g.First());
        var alerts = await db.Alerts.Where(a => ids.Contains(a.SchoolId) && a.AcknowledgedAt == null)
            .GroupBy(a => a.SchoolId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
        return schools.Select(s => new TileResponse(
            s.Id, s.Slug, s.DisplayName, s.ShortName, s.PrimaryColor, s.Status, s.PrimaryDomain, s.AppVersion, s.TargetVersion,
            s.HostId is { } h ? hosts.GetValueOrDefault(h) : null, s.IsCanary, s.IsVersionPinned,
            latest.TryGetValue(s.Id, out var t) ? ToTelemetry(t) : null,
            openRuns.TryGetValue(s.Id, out var r) && (r.IsOpen || s.Status == SchoolStatus.Failed) ? Summary(r, engine) : null,
            alerts.GetValueOrDefault(s.Id), s.Categories,
            s.CustomCategoryNames.Select(pair => new CategoryName(pair.Key, pair.Value)).OrderBy(c => c.Name).ToList(),
            s.PublicDomain, ToPackage(s))).ToList();
    }

    private static RunSummary Summary(ProvisioningRun run, ProvisioningEngine engine)
    {
        var plan = engine.PlanFor(run.Kind);
        return new RunSummary(run.Id, run.Kind, run.Status, run.CurrentStep, plan.Count,
            run.CurrentStep < plan.Count ? plan[run.CurrentStep].Name : null, run.Error, run.CreatedAt);
    }

    private static TelemetryResponse ToTelemetry(TelemetrySnapshot t) => new(
        t.TakenAt, t.Reachable, t.Health, t.HealthDetail, t.AppVersion, t.ActiveStudents, t.ActiveTeachers, t.ActiveEnrollments,
        t.LastBackupAt, t.DatabaseSizeBytes, t.PendingNotifications, t.FailedNotifications, t.Error, t.ActiveStaffUsers);

    private static PackageResponse ToPackage(School school) =>
        new(school.StaffSeatLimit, school.PricePerSeat, school.AddonPrices, school.Currency, school.YearlyTotal);

    private static async Task<IResult> DetailAsync(Guid id, PlatformDbContext db, ProvisioningEngine engine)
    {
        var school = await db.Schools.SingleOrDefaultAsync(s => s.Id == id) ?? throw new NotFoundException("Okul bulunamadı.");
        var tile = (await TilesAsync(db, engine, [school])).Single();
        var runs = await db.Runs.Where(r => r.SchoolId == id).OrderByDescending(r => r.CreatedAt).Take(10).ToListAsync();
        var runIds = runs.Select(r => r.Id).ToList();
        var steps = await db.Steps.Where(s => runIds.Contains(s.RunId)).OrderBy(s => s.Step).ThenBy(s => s.Attempt).ToListAsync();
        var created = await db.AuditLogs.Where(a => a.SchoolId == id && a.Action == "school.created").Select(a => a.AfterJson).FirstOrDefaultAsync();
        var adminEmail = created is null ? null : JsonDocument.Parse(created).RootElement.GetProperty("adminEmail").GetString();
        var secretNames = await db.SchoolSecrets.Where(s => s.SchoolId == id).Select(s => s.Name).OrderBy(n => n).ToListAsync();
        var history = await db.Telemetry.Where(t => t.SchoolId == id).OrderByDescending(t => t.TakenAt).Take(48).ToListAsync();
        var alerts = await db.Alerts.Where(a => a.SchoolId == id).OrderByDescending(a => a.ReceivedAt).Take(20).ToListAsync();

        return Results.Ok(new DetailResponse(
            tile, school.Modules, adminEmail, secretNames,
            runs.Select(r => new RunResponse(r.Id, r.Kind, r.Status, r.CurrentStep, engine.PlanFor(r.Kind).Select(p => p.Name).ToList(),
                r.TargetVersion, r.Error, r.CreatedAt, r.FinishedAt,
                steps.Where(s => s.RunId == r.Id).Select(s => new StepResponse(s.Step, s.Name, s.Status, s.Attempt, s.Log, s.StartedAt, s.FinishedAt)).ToList())).ToList(),
            history.Select(ToTelemetry).ToList(),
            alerts.Select(a => new AlertResponse(a.Id, a.Kind, a.Level, a.Detail, a.OccurredAt, a.AcknowledgedAt)).ToList(),
            school.CreatedAt,
            await SchoolDomains.ResponseAsync(db, school)));
    }

    private static async Task<IResult> RevealAdminPasswordAsync(Guid id, ClaimsPrincipal principal, PlatformDbContext db, SecretVault vault, TimeProvider time)
    {
        var password = await vault.GetAsync(id, SecretNames.AdminTemporaryPassword) ?? throw new NotFoundException("Geçici parola yok.");
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "school.admin_password_revealed", id, time.GetUtcNow()));
        await db.SaveChangesAsync();
        return Results.Ok(new { temporaryPassword = password });
    }

    private static async Task EnsureNoOpenRunAsync(PlatformDbContext db, Guid schoolId)
    {
        if (await db.Runs.AnyAsync(r => r.SchoolId == schoolId && (r.Status == RunStatus.Pending || r.Status == RunStatus.Running)))
            throw new ConflictException("Bu okulda süren bir işlem var; bitmesini bekleyin.");
    }

    private static async Task<IResult> EnqueueAsync(Guid id, RunKind kind, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time, Func<School, string?> guard)
    {
        var school = await db.Schools.SingleOrDefaultAsync(s => s.Id == id) ?? throw new NotFoundException("Okul bulunamadı.");
        if (guard(school) is { } problem) throw new ConflictException(problem);
        await EnsureNoOpenRunAsync(db, id);
        var run = ProvisioningRun.Create(id, kind, time.GetUtcNow());
        db.Runs.Add(run);
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), $"school.{kind.ToString().ToLowerInvariant()}_requested", id, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { school.Slug, runId = run.Id })));
        await db.SaveChangesAsync();
        return Results.Accepted($"/api/schools/{id}", new { runId = run.Id });
    }

    private static async Task<IResult> RetryAsync(Guid id, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        await EnsureNoOpenRunAsync(db, id);
        var run = await db.Runs.Where(r => r.SchoolId == id).OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync()
            ?? throw new NotFoundException("İşlem bulunamadı.");
        run.Retry(time.GetUtcNow());
        var school = await db.Schools.SingleAsync(s => s.Id == id);
        if (run.Kind == RunKind.Create) school.MarkProvisioning(time.GetUtcNow());
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "run.retried", id, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { run.Id, kind = run.Kind.ToString(), fromStep = run.CurrentStep })));
        await db.SaveChangesAsync();
        return Results.Accepted($"/api/schools/{id}", new { runId = run.Id });
    }

    // Tek okul güncellemesi (filo dağıtımı dışında). Geri sürüme dönüş yalnızca açık bir rollback'le
    // olur; burada yalnızca ileri sürüm kabul edilir (§16.4).
    private static async Task<IResult> UpgradeAsync(Guid id, UpgradeRequest request, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        var school = await db.Schools.SingleOrDefaultAsync(s => s.Id == id) ?? throw new NotFoundException("Okul bulunamadı.");
        if (school.Status != SchoolStatus.Active) throw new ConflictException("Yalnızca etkin okul güncellenebilir.");
        var release = await db.Releases.SingleOrDefaultAsync(r => r.Version == request.Version && !r.IsWithdrawn)
            ?? throw ValidationFailedException.For("version", "Sürüm bulunamadı ya da geri çekilmiş.");
        if (school.AppVersion is not null && Release.Compare(release.Version, school.AppVersion) <= 0)
            throw new ConflictException($"Okul zaten {school.AppVersion} sürümünde; yalnızca ileri sürüme geçilebilir.");
        // Yeni sürümün kataloğunda okulun modülleri hâlâ var mı; standart pakete girenler seçimden düşer.
        // Okulun kaydı değiştirilmez: güncelleme geri alınırsa eski sürümün seçimi geçerli kalmalı.
        ValidateModules(school.Modules, Releases.Catalog(release));
        await EnsureNoOpenRunAsync(db, id);
        school.SetTargetVersion(release.Version, time.GetUtcNow());
        var run = ProvisioningRun.Create(id, RunKind.Upgrade, time.GetUtcNow(), release.Version);
        db.Runs.Add(run);
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "school.upgrade_requested", id, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { from = school.AppVersion, to = release.Version })));
        await db.SaveChangesAsync();
        return Results.Accepted($"/api/schools/{id}", new { runId = run.Id });
    }

    private static async Task<IResult> FlagsAsync(Guid id, FlagsRequest request, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        var school = await db.Schools.SingleOrDefaultAsync(s => s.Id == id) ?? throw new NotFoundException("Okul bulunamadı.");
        var before = JsonSerializer.Serialize(new { school.IsCanary, school.IsVersionPinned });
        school.SetCanary(request.IsCanary, time.GetUtcNow());
        school.SetVersionPinned(request.IsVersionPinned, time.GetUtcNow());
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "school.flags_changed", id, time.GetUtcNow(),
            beforeJson: before, afterJson: JsonSerializer.Serialize(new { request.IsCanary, request.IsVersionPinned })));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // Modül/entegrasyon değişikliği: okulun .env'i yeniden yazılır (Reconfigure). Kapatmak veri silmez.
    private static async Task<IResult> ModulesAsync(
        Guid id, ModulesRequest request, ClaimsPrincipal principal, PlatformDbContext db, SecretVault vault, IConfiguration config, TimeProvider time)
    {
        var school = await db.Schools.SingleOrDefaultAsync(s => s.Id == id) ?? throw new NotFoundException("Okul bulunamadı.");
        if (school.Status != SchoolStatus.Active) throw new ConflictException("Yalnızca etkin okulun modülleri değiştirilebilir.");
        var release = await db.Releases.SingleAsync(r => r.Version == school.AppVersion);
        var modules = ValidateModules(request.Modules ?? [], Releases.Catalog(release));
        ValidateIntegrations(request.Integrations);
        await EnsureNoOpenRunAsync(db, id);
        var before = JsonSerializer.Serialize(new { modules = school.Modules, package = ToPackage(school) });
        school.SetModules(modules, time.GetUtcNow());
        // Paketteki eklenti fiyatları açık eklentilerle hizalanır: kapatılanın fiyatı düşer, yeni açılan
        // liste fiyatıyla girer (operatör Paket sekmesinden değiştirir). Sınırsız (eski) okulda fiyat da yok.
        if (school.StaffSeatLimit is not null)
        {
            var listPrices = Pricing.Defaults(config).AddonPrices;
            var addonPrices = modules.ToDictionary(module => module,
                module => school.AddonPrices.TryGetValue(module, out var price) ? price : listPrices.GetValueOrDefault(module));
            school.SetPackage(school.StaffSeatLimit, school.PricePerSeat, addonPrices, time.GetUtcNow());
        }
        foreach (var (key, value) in request.Integrations ?? new Dictionary<string, string>())
            if (!string.IsNullOrWhiteSpace(value)) await vault.SetAsync(id, SecretNames.IntegrationPrefix + key, value.Trim());
        var run = ProvisioningRun.Create(id, RunKind.Reconfigure, time.GetUtcNow());
        db.Runs.Add(run);
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "school.modules_changed", id, time.GetUtcNow(),
            beforeJson: before, afterJson: JsonSerializer.Serialize(new { modules, package = ToPackage(school), integrations = (request.Integrations ?? new Dictionary<string, string>()).Keys.Order().ToList() })));
        await db.SaveChangesAsync();
        return Results.Accepted($"/api/schools/{id}", new { runId = run.Id });
    }

    // Paket değişikliği. Kullanıcı sınırı okulun .env'ine yazıldığından sınır değişince okul yeniden
    // yapılandırılır (yalnızca etkin okulda); fiyat değişikliği yalnızca platform kaydıdır. Sınırı
    // kullanılan hesap sayısının altına indirmek var olan hesapları kapatmaz, yalnızca yenisini engeller.
    private static async Task<IResult> PackageAsync(Guid id, PackageUpdateRequest request, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        var school = await db.Schools.SingleOrDefaultAsync(s => s.Id == id) ?? throw new NotFoundException("Okul bulunamadı.");
        if (school.Status is SchoolStatus.Destroyed or SchoolStatus.PendingDestroy) throw new ConflictException("Silinen okulun paketi değiştirilemez.");
        if (request.StaffSeats is null) throw ValidationFailedException.For("package.staffSeats", "Kullanıcı sayısı (personel hesabı) girilmeli.");
        var seatsChanged = request.StaffSeats != school.StaffSeatLimit;
        if (seatsChanged)
        {
            if (school.Status != SchoolStatus.Active) throw new ConflictException("Kullanıcı sayısı yalnızca etkin okulda değiştirilebilir.");
            await EnsureNoOpenRunAsync(db, id);
        }

        var before = JsonSerializer.Serialize(ToPackage(school));
        school.SetPackage(request.StaffSeats, request.PricePerSeat, request.AddonPrices ?? new Dictionary<string, decimal>(), time.GetUtcNow());
        ProvisioningRun? run = null;
        if (seatsChanged)
        {
            run = ProvisioningRun.Create(id, RunKind.Reconfigure, time.GetUtcNow());
            db.Runs.Add(run);
        }
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "school.package_changed", id, time.GetUtcNow(),
            beforeJson: before, afterJson: JsonSerializer.Serialize(ToPackage(school))));
        await db.SaveChangesAsync();
        return Results.Ok(new { package = ToPackage(school), runId = run?.Id });
    }
}
