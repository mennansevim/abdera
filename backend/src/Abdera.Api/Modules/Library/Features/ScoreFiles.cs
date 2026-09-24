using System.Security.Claims;
using Abdera.Api.Modules.Library.Domain;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Library.Features;

// Nota PDF'leri. Kitap eserleri yayıncı izniyle yalnızca okul içinde kullanılır: dosyayı yalnızca
// giriş yapmış öğretmen ve yönetici görür. Kitap eserinin (book-*) PDF'ini yalnızca yönetici,
// kütüphaneye eklenen eserin (piece-*) PDF'ini onu ekleyen öğretmen veya yönetici yükler/siler.
// Veli ve anonim istekler 401/403 alır; herkese açık /kutuphane sayfası bu uçları hiç çağırmaz.
public static class ScoreFiles
{
    public record Summary(string EntryId, int? PageCount, int SizeBytes, string Version);

    public static void MapScoreFiles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/library/score-files", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapGet("/api/library/score-files/{entryId}", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapPut("/api/library/score-files/{entryId}", UploadAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin)
            .DisableAntiforgery();

        app.MapDelete("/api/library/score-files/{entryId}", DeleteAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    // Kütüphane ekranı hangi eserin PDF'i olduğunu bilmek için çağırır. Content kolonu
    // projeksiyona girmediği için dosya baytları veritabanından okunmaz.
    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var files = await db.ScoreFiles.AsNoTracking()
            .OrderBy(f => f.EntryId)
            .Select(f => new { f.EntryId, f.PageCount, f.SizeBytes, f.Version })
            .ToListAsync();
        return Results.Ok(files.Select(f => new Summary(f.EntryId, f.PageCount, f.SizeBytes, f.Version.ToString("N"))));
    }

    private static async Task<IResult> GetAsync(string entryId, HttpContext http, AbderaDbContext db)
    {
        if (!ScoreFile.IsValidEntryId(entryId)) return Results.NotFound();
        var file = await db.ScoreFiles.AsNoTracking().SingleOrDefaultAsync(f => f.EntryId == entryId);
        if (file is null) return Results.NotFound();

        var etag = $"\"{file.Version:N}\"";
        if (http.Request.Headers.IfNoneMatch.ToString() == etag)
            return Results.StatusCode(StatusCodes.Status304NotModified);

        http.Response.Headers.ETag = etag;
        // private: paylaşılan önbellek/CDN oturuma bağlı bu dosyayı saklayıp başkasına sunmasın.
        http.Response.Headers.CacheControl = "private, max-age=3600";
        // inline: tarayıcının PDF görüntüleyicisinde açılır, indirme penceresi çıkmaz.
        return Results.File(file.Content, ScoreFile.ContentType, fileDownloadName: null, enableRangeProcessing: true);
    }

    private static async Task<IResult> UploadAsync(
        string entryId, IFormFile? file, [FromForm] int? pageCount, ClaimsPrincipal user, AbderaDbContext db, IClock clock)
    {
        await EnsureCanChangeAsync(entryId, user, db);

        if (file is null || file.Length == 0)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["file"] = ["PDF dosyası gönderilmedi."],
            });

        // Sınır belleğe almadan ÖNCE kontrol edilir (StudentPhotos ile aynı gerekçe).
        if (file.Length > ScoreFile.MaxBytes)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["file"] = ["Eser PDF'i en fazla 4 MB olabilir."],
            });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var content = buffer.ToArray();

        var now = clock.UtcNow;
        var uploadedBy = AuthContext.GetUserId(user);
        var existing = await db.ScoreFiles.SingleOrDefaultAsync(f => f.EntryId == entryId);
        if (existing is null)
        {
            existing = ScoreFile.Create(entryId, content, pageCount, uploadedBy, now);
            db.ScoreFiles.Add(existing);
        }
        else
        {
            existing.Replace(content, pageCount, uploadedBy, now);
        }

        await db.SaveChangesAsync();
        return Results.Ok(new Summary(existing.EntryId, existing.PageCount, existing.SizeBytes, existing.Version.ToString("N")));
    }

    private static async Task<IResult> DeleteAsync(string entryId, ClaimsPrincipal user, AbderaDbContext db)
    {
        await EnsureCanChangeAsync(entryId, user, db);
        var file = await db.ScoreFiles.SingleOrDefaultAsync(f => f.EntryId == entryId);
        if (file is null) return Results.NoContent();

        db.ScoreFiles.Remove(file);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task EnsureCanChangeAsync(string entryId, ClaimsPrincipal user, AbderaDbContext db)
    {
        if (!ScoreFile.IsValidEntryId(entryId))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["entryId"] = ["Geçersiz eser kimliği."] });

        if (ScoreFile.IsBookEntry(entryId))
        {
            if (!AuthContext.IsAdmin(user))
                throw new ForbiddenException("Kitap eserlerinin PDF'ini yalnızca yönetici değiştirebilir.");
            return;
        }

        var pieceId = Guid.ParseExact(entryId["piece-".Length..], "N");
        await LibraryPieces.FindEditableAsync(pieceId, user, db);
    }
}
