using System.Security.Claims;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Auth.Features;

public static class Me
{
    // InstrumentIds: Teacher oturumunda kendi TeacherInstruments'ı (Admin'de her zaman boş
    // dizi) - Takvim ekranındaki enstrüman filtresini "yalnızca kendi branşı" ile
    // sınırlamak için (kullanıcı isteği: "öğretmen sadece kendi branşını görebilir").
    // Bu tamamen kendi verisi, /api/teachers zaten herkese açık olsa da ayrı bir istek
    // yerine buraya eklendi - istemci zaten her açılışta bu yanıtı okuyor.
    // TeacherId: öğretmen artık kendi öğrencisini ekleyebildiği için istemcinin kendi
    // teacher kaydının id'sine ihtiyacı var (POST /api/teachers/{teacherId}/students).
    // Admin'de null.
    public record Response(
        Guid Id, string Email, UserRole Role, bool MustChangePassword,
        Guid[] InstrumentIds, Guid? TeacherId);

    public static void MapMe(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/me", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(ClaimsPrincipal principal, AbderaDbContext db)
    {
        var id = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Claim'ler yalnızca giriş anındaki bilgiyi taşır; MustChangePassword aynı oturum
        // içinde ChangePassword ile değişebileceğinden veritabanından taze okunur.
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.IsActive)
            ?? throw new ForbiddenException("Hesap artık aktif değil.");

        var teacherId = user.Role == UserRole.Teacher
            ? await db.Teachers.AsNoTracking()
                .Where(teacher => teacher.UserId == user.Id)
                .Select(teacher => (Guid?)teacher.Id)
                .SingleOrDefaultAsync()
            : null;

        var instrumentIds = teacherId is { } scopedTeacherId
            ? await db.TeacherInstruments.AsNoTracking()
                .Where(ti => ti.TeacherId == scopedTeacherId)
                .Select(ti => ti.InstrumentId)
                .ToArrayAsync()
            : [];

        return Results.Ok(new Response(
            user.Id, user.Email, user.Role, user.MustChangePassword,
            instrumentIds, teacherId));
    }
}
