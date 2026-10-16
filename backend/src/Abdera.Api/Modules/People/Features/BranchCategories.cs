using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Abdera.Api.Shared.Branches;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// Okulun eğitim alanları: sabit katalog (BranchCatalog) + okulun kendi açtıkları (CustomBranchCategory).
// Okuma iki role açık (formlar), yazma yalnızca Admin. Katalog alanı değiştirilemez; okul alanı
// adı/terimiyle düzenlenir, hiçbir alt birim kullanmıyorsa silinir.
public static class BranchCategories
{
    public record CategoryResponse(string Key, string Name, string UnitLabel, string UnitLabelPlural, IReadOnlyList<string> Suggestions, bool IsCustom, bool InUse);
    public record CreateRequest(string Name, string? UnitLabel, string? UnitLabelPlural);

    public static void MapBranchCategories(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/branch-categories", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/branch-categories", CreateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPut("/api/branch-categories/{key}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapDelete("/api/branch-categories/{key}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    // Katalog sırası, ardından okulun alanları (ada göre).
    public static async Task<IReadOnlyList<(BranchCategory Category, bool IsCustom)>> LoadAllAsync(AbderaDbContext db)
    {
        var custom = await db.CustomBranchCategories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        return BranchCatalog.All.Select(c => (c, false)).Concat(custom.Select(c => (c.ToCategory(), true))).ToList();
    }

    public static async Task<IReadOnlyList<CategoryResponse>> ResponsesAsync(AbderaDbContext db)
    {
        var inUse = await db.Instruments.Where(i => i.IsActive).Select(i => i.Category).Distinct().ToListAsync();
        return (await LoadAllAsync(db))
            .Select(x => new CategoryResponse(x.Category.Key, x.Category.Name, x.Category.UnitLabel, x.Category.UnitLabelPlural,
                x.Category.Suggestions, x.IsCustom, inUse.Contains(x.Category.Key)))
            .ToList();
    }

    // Katalog anahtarı ya da okulda var olan alan; değilse alan doğrulama hatası.
    public static async Task<string> EnsureKnownAsync(AbderaDbContext db, string? key, string field = "category")
    {
        if (BranchCatalog.Find(key) is { } known) return known.Key;
        var normalized = key?.Trim().ToLowerInvariant();
        if (BranchCatalog.IsCustomKey(normalized)
            && (await db.CustomBranchCategories.AnyAsync(c => c.Key == normalized) || db.CustomBranchCategories.Local.Any(c => c.Key == normalized)))
            return normalized!;
        throw new ValidationFailedException(new Dictionary<string, string[]> { [field] = ["Geçerli bir alan seçilmeli."] });
    }

    // Ayarlar ve kurulum aynı kuralla ekler. Anahtar verilmezse addan türetilir (çakışırsa -2, -3...).
    public static async Task<CustomBranchCategory> AddAsync(AbderaDbContext db, string? key, string name, string? unitLabel, string? unitLabelPlural, DateTimeOffset now)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length is 0 or > 60) throw Invalid("name", "Alan adı 1-60 karakter olmalı.");
        if (BranchCatalog.All.Any(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            || await db.CustomBranchCategories.AnyAsync(c => c.Name == trimmed) || db.CustomBranchCategories.Local.Any(c => c.Name == trimmed))
            throw new ConflictException($"'{trimmed}' adlı bir alan zaten var.");
        string finalKey;
        if (key is not null)
        {
            finalKey = key.Trim().ToLowerInvariant();
            if (!BranchCatalog.IsCustomKey(finalKey)) throw Invalid("key", "Alan anahtarı 'c-' ile başlamalı (küçük harf, rakam, tire).");
            if (await db.CustomBranchCategories.AnyAsync(c => c.Key == finalKey)) throw new ConflictException($"'{finalKey}' anahtarlı bir alan zaten var.");
        }
        else
        {
            var baseKey = BranchCatalog.CustomKeyFor(trimmed);
            finalKey = baseKey;
            for (var n = 2; await db.CustomBranchCategories.AnyAsync(c => c.Key == finalKey) || db.CustomBranchCategories.Local.Any(c => c.Key == finalKey); n++)
                finalKey = (baseKey.Length > 36 ? baseKey[..36] : baseKey) + "-" + n;
        }
        if ((unitLabel?.Trim().Length ?? 0) > 40 || (unitLabelPlural?.Trim().Length ?? 0) > 40) throw Invalid("unitLabel", "Terim en fazla 40 karakter olabilir.");
        var category = CustomBranchCategory.Create(finalKey, trimmed, unitLabel, unitLabelPlural, now);
        db.CustomBranchCategories.Add(category);
        return category;
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db) => Results.Ok(await ResponsesAsync(db));

    private static async Task<IResult> CreateAsync(CreateRequest request, AbderaDbContext db, IClock clock)
    {
        var category = await AddAsync(db, null, request.Name, request.UnitLabel, request.UnitLabelPlural, clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Created($"/api/branch-categories/{category.Key}",
            new CategoryResponse(category.Key, category.Name, category.UnitLabel, category.UnitLabelPlural, [], true, false));
    }

    private static async Task<IResult> UpdateAsync(string key, CreateRequest request, AbderaDbContext db, IClock clock)
    {
        var category = await db.CustomBranchCategories.SingleOrDefaultAsync(c => c.Key == key)
            ?? throw new NotFoundException("Alan bulunamadı (katalog alanları değiştirilemez).");
        var trimmed = request.Name?.Trim() ?? "";
        if (trimmed.Length is 0 or > 60) throw Invalid("name", "Alan adı 1-60 karakter olmalı.");
        if (BranchCatalog.All.Any(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            || await db.CustomBranchCategories.AnyAsync(c => c.Key != key && c.Name == trimmed))
            throw new ConflictException($"'{trimmed}' adlı bir alan zaten var.");
        if ((request.UnitLabel?.Trim().Length ?? 0) > 40 || (request.UnitLabelPlural?.Trim().Length ?? 0) > 40) throw Invalid("unitLabel", "Terim en fazla 40 karakter olabilir.");
        category.Update(trimmed, request.UnitLabel, request.UnitLabelPlural, clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(new CategoryResponse(category.Key, category.Name, category.UnitLabel, category.UnitLabelPlural, [], true,
            await db.Instruments.AnyAsync(i => i.Category == key && i.IsActive)));
    }

    private static async Task<IResult> DeleteAsync(string key, AbderaDbContext db)
    {
        var category = await db.CustomBranchCategories.SingleOrDefaultAsync(c => c.Key == key)
            ?? throw new NotFoundException("Alan bulunamadı (katalog alanları silinemez).");
        if (await db.Instruments.AnyAsync(i => i.Category == key))
            throw new ConflictException("Bu alanda alt birim var; önce onları başka alana taşı ya da alanı olduğu gibi bırak.");
        db.CustomBranchCategories.Remove(category);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static ValidationFailedException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
