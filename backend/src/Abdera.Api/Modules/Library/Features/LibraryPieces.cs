using System.Security.Claims;
using Abdera.Api.Modules.Library.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Library.Features;

// Kütüphaneye öğretmen/yöneticinin eklediği eserler (katalogda olmayan repertuvar; örn. bateri
// ritim sayfaları). Yalnızca panelde görünür. Ekleyen öğretmen ve yönetici düzenler/siler;
// PDF'i ScoreFiles üzerinden "piece-<id>" kimliğiyle yüklenir.
public static class LibraryPieces
{
    public record Request(string Title, string? Composer, string Instrument, string Category, int? Level, string? Notes);

    public record PieceResponse(
        Guid Id, string EntryId, string Title, string Composer, string Instrument, string Category,
        int? Level, string? Notes, bool CanEdit, DateTimeOffset CreatedAt);

    public static void MapLibraryPieces(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library/pieces").RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, AbderaDbContext db)
    {
        var userId = AuthContext.GetUserId(user);
        var isAdmin = AuthContext.IsAdmin(user);
        var pieces = await db.LibraryPieces.AsNoTracking().OrderBy(p => p.Title).ToListAsync();
        return Results.Ok(pieces.Select(p => ToResponse(p, p.CanBeEditedBy(userId, isAdmin))));
    }

    private static async Task<IResult> CreateAsync(Request request, ClaimsPrincipal user, AbderaDbContext db, IClock clock)
    {
        var piece = LibraryPiece.Create(request.Title, request.Composer ?? string.Empty, request.Instrument, request.Category,
            request.Level, request.Notes, AuthContext.GetUserId(user), clock.UtcNow);
        db.LibraryPieces.Add(piece);
        await db.SaveChangesAsync();
        return Results.Created($"/api/library/pieces/{piece.Id}", ToResponse(piece, canEdit: true));
    }

    private static async Task<IResult> UpdateAsync(Guid id, Request request, ClaimsPrincipal user, AbderaDbContext db, IClock clock)
    {
        var piece = await FindEditableAsync(id, user, db);
        piece.Update(request.Title, request.Composer ?? string.Empty, request.Instrument, request.Category, request.Level, request.Notes, clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(piece, canEdit: true));
    }

    // Eserin PDF'i de gider. Öğrencilere yapılmış öneriler kalır: eser adı öneri satırına
    // donduğu için öğrencinin listesi okunur kalmaya devam eder.
    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal user, AbderaDbContext db)
    {
        var piece = await FindEditableAsync(id, user, db);
        var file = await db.ScoreFiles.SingleOrDefaultAsync(f => f.EntryId == piece.EntryId);
        if (file is not null) db.ScoreFiles.Remove(file);
        db.LibraryPieces.Remove(piece);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    internal static async Task<LibraryPiece> FindEditableAsync(Guid id, ClaimsPrincipal user, AbderaDbContext db)
    {
        var piece = await db.LibraryPieces.SingleOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException("Eser bulunamadı.");
        if (!piece.CanBeEditedBy(AuthContext.GetUserId(user), AuthContext.IsAdmin(user)))
            throw new ForbiddenException("Bu eseri yalnızca ekleyen öğretmen veya yönetici değiştirebilir.");
        return piece;
    }

    private static PieceResponse ToResponse(LibraryPiece p, bool canEdit) =>
        new(p.Id, p.EntryId, p.Title, p.Composer, p.Instrument, p.Category, p.Level, p.Notes, canEdit, p.CreatedAt);
}
