using Abdera.Api.Modules.Show.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Show.Features;

// Öğrenci fotoğrafı yükleme ve sunma. Sahne ekranının "sırada kim var" göstergesi buna
// dayanıyor (bkz. StudentPhoto.cs'teki depolama gerekçesi).
public static class StudentPhotos
{
    public record UploadResponse(Guid StudentId, string Version);

    public static void MapStudentPhotos(this IEndpointRouteBuilder app)
    {
        // Fotoğrafı öğretmen de görebilir (kulis ekranı), yalnızca yönetici değiştirebilir.
        app.MapGet("/api/students/{studentId:guid}/photo", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapPut("/api/students/{studentId:guid}/photo", UploadAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly)
            .DisableAntiforgery();

        app.MapDelete("/api/students/{studentId:guid}/photo", DeleteAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> GetAsync(Guid studentId, HttpContext http, AbderaDbContext db)
    {
        var photo = await db.StudentPhotos.AsNoTracking().SingleOrDefaultAsync(item => item.StudentId == studentId);
        if (photo is null) return Results.NotFound();

        // Sahne ekranı saniyede bir yenileniyor; ETag olmadan her turda yüzlerce KB
        // yeniden inerdi. Sürüm yalnızca fotoğraf değişince değişir.
        var etag = $"\"{photo.Version:N}\"";
        if (http.Request.Headers.IfNoneMatch.ToString() == etag)
            return Results.StatusCode(StatusCodes.Status304NotModified);

        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.File(photo.Content, photo.ContentType);
    }

    private static async Task<IResult> UploadAsync(
        Guid studentId, IFormFile? file, AbderaDbContext db, IClock clock)
    {
        if (file is null || file.Length == 0)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["file"] = ["Fotoğraf dosyası gönderilmedi."],
            });

        // Boyut sınırı belleğe almadan ÖNCE kontrol edilir - 2 MB'lık kuralı okuduktan
        // sonra uygulamak, sınırın var olma sebebini ortadan kaldırırdı.
        if (file.Length > StudentPhoto.MaxBytes)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["file"] = ["Fotoğraf en fazla 2 MB olabilir."],
            });

        if (!await db.Students.AnyAsync(student => student.Id == studentId))
            throw new NotFoundException("Öğrenci bulunamadı.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var content = buffer.ToArray();

        var now = clock.UtcNow;
        var photo = await db.StudentPhotos.SingleOrDefaultAsync(item => item.StudentId == studentId);
        if (photo is null)
        {
            photo = StudentPhoto.Create(studentId, file.ContentType, content, now);
            db.StudentPhotos.Add(photo);
        }
        else
        {
            photo.Replace(file.ContentType, content, now);
        }

        await db.SaveChangesAsync();
        return Results.Ok(new UploadResponse(studentId, photo.Version.ToString("N")));
    }

    private static async Task<IResult> DeleteAsync(Guid studentId, AbderaDbContext db)
    {
        var photo = await db.StudentPhotos.SingleOrDefaultAsync(item => item.StudentId == studentId);
        if (photo is null) return Results.NoContent();

        db.StudentPhotos.Remove(photo);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
