using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Abdera.Api.Shared.Branches;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// docs/07-api.md: /api/instruments - okulun alt birimleri (branş: enstrüman, dans türü, spor branşı...;
// BranchCatalog). Kişisel/mali veri değil: okuma her iki role açık (form/dropdown), yazma yalnızca Admin.
// Pasif alt birim listede kalır (eski kayıtlar adını göstersin) ama yeni kayıtta seçilmez; silinmez.
public static class Instruments
{
    public record CreateRequest(string Name, string? Code, string? Category);
    public record UpdateRequest(string Name, string Category, bool IsActive);
    public record InstrumentResponse(Guid Id, string Name, string Code, string Category, bool IsActive);

    public static void MapInstruments(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/instruments", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/instruments", CreateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPut("/api/instruments/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var instruments = await db.Instruments
            .OrderBy(i => i.Name)
            .Select(i => new InstrumentResponse(i.Id, i.Name, i.Code, i.Category, i.IsActive))
            .ToListAsync();

        return Results.Ok(instruments);
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, AbderaDbContext db)
    {
        var instrument = await AddAsync(db, request.Name, request.Code, request.Category ?? BranchCatalog.Other);
        await db.SaveChangesAsync();
        return Results.Created($"/api/instruments/{instrument.Id}", ToResponse(instrument));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateRequest request, AbderaDbContext db)
    {
        var instrument = await db.Instruments.SingleOrDefaultAsync(i => i.Id == id) ?? throw new NotFoundException("Alt birim bulunamadı.");
        Validate(request.Name);
        var category = await BranchCategories.EnsureKnownAsync(db, request.Category);
        var name = request.Name.Trim();
        if (await db.Instruments.AnyAsync(i => i.Id != id && i.Name == name))
            throw new ConflictException($"'{name}' adlı bir alt birim zaten var.");
        instrument.Update(name, category);
        instrument.SetActive(request.IsActive);
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(instrument));
    }

    // Kurulum (InstanceProvisioning) ve Ayarlar aynı kuralla ekler: ad ve kod okul içinde benzersiz.
    public static async Task<Instrument> AddAsync(AbderaDbContext db, string name, string? code, string category)
    {
        Validate(name);
        category = await BranchCategories.EnsureKnownAsync(db, category);
        var trimmed = name.Trim();
        if (await db.Instruments.AnyAsync(i => i.Name == trimmed) || db.Instruments.Local.Any(i => i.Name == trimmed))
            throw new ConflictException($"'{trimmed}' adlı bir alt birim zaten var.");
        var baseCode = string.IsNullOrWhiteSpace(code) ? BranchCatalog.CodeFor(trimmed) : code.Trim().ToUpperInvariant();
        var unique = baseCode;
        for (var n = 2; await db.Instruments.AnyAsync(i => i.Code == unique) || db.Instruments.Local.Any(i => i.Code == unique); n++)
        {
            if (!string.IsNullOrWhiteSpace(code)) throw new ConflictException($"'{baseCode}' kodlu bir alt birim zaten var.");
            var suffix = $"_{n}";
            unique = (baseCode.Length + suffix.Length > 20 ? baseCode[..(20 - suffix.Length)] : baseCode) + suffix;
        }
        var instrument = Instrument.Create(trimmed, unique, category);
        db.Instruments.Add(instrument);
        return instrument;
    }

    private static void Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["name"] = ["Ad 1-100 karakter olmalı."] });
    }

    private static InstrumentResponse ToResponse(Instrument i) => new(i.Id, i.Name, i.Code, i.Category, i.IsActive);
}
