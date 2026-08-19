using System.Security.Claims;
using Abdera.Api.Modules.Auth.Domain;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Shared;

// docs/04-permissions.md: "Her TEACHER isteğinde hedef kaynağın teacher_id'si oturumdan
// çözümlenir, URL'deki id'ye güvenilmez." Bu sorgu People/Scheduling/Attendance/Billing
// modüllerinin birçok handler'ında tekrarlandığı için tek yerde toplanır (DRY).
public static class AuthContext
{
    public static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static bool IsAdmin(ClaimsPrincipal principal) =>
        principal.IsInRole(nameof(UserRole.Admin));

    // Admin ise null döner (kapsamı yok - her şeyi görür); Teacher ise kendi Teacher.Id'sini
    // döner. Teacher rolündeki bir kullanıcının henüz Teacher kaydı yoksa (veri tutarsızlığı)
    // ForbiddenException fırlatılır - sessizce boş sonuç dönmek yanlış güven verir.
    public static async Task<Guid?> ResolveTeacherScopeAsync(ClaimsPrincipal principal, AbderaDbContext db)
    {
        if (IsAdmin(principal)) return null;

        var userId = GetUserId(principal);
        var teacherId = await db.Teachers
            .Where(t => t.UserId == userId)
            .Select(t => (Guid?)t.Id)
            .SingleOrDefaultAsync();

        return teacherId ?? throw new ForbiddenException("Bu kullanıcıya bağlı bir öğretmen kaydı bulunamadı.");
    }
}
