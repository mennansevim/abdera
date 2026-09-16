using System.Security.Claims;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// Öğretmen artık kendi öğrencisini ekleyebiliyor/düzenleyebiliyor (docs/10-decisions.md J1).
// "Kendi" kelimesinin tek tanımı burada: öğretmenin AKTİF bir kurs kaydı üzerinden bağlı
// olduğu öğrenci. Bu kontrol her uçta tekrar yazılmak yerine tek yerde tutuluyor - bir
// yerde unutulursa öğretmen başka bir öğretmenin öğrencisini düzenleyebilirdi.
//
// Progress modülündeki ProgressAuthorization ile aynı desen; oradaki kopyası kalıyor çünkü
// modüller arası doğrudan bağımlılık kurmuyoruz (CLAUDE.md modül sınırı kuralı).
internal static class PeopleAuthorization
{
    // Admin için null döner (kapsam yok), öğretmen için kendi teacherId'si.
    public static async Task<Guid?> EnsureStudentAccessAsync(
        Guid studentId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        if (!await db.Students.AnyAsync(student => student.Id == studentId))
            throw new NotFoundException("Öğrenci bulunamadı.");

        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherId is null) return null;

        var teaches = await db.Enrollments.AnyAsync(enrollment =>
            enrollment.StudentId == studentId &&
            enrollment.TeacherId == teacherId &&
            enrollment.Status == Domain.EnrollmentStatus.Active);

        if (!teaches) throw new ForbiddenException("Bu öğrenci size atanmamış.");
        return teacherId;
    }

    // Veli, öğretmenin kendi öğrencilerinden en az birine bağlı olmalı. Aksi halde bir
    // öğretmen okuldaki herhangi bir velinin telefonunu düzenleyebilirdi.
    public static async Task<Guid?> EnsureGuardianAccessAsync(
        Guid guardianId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        if (!await db.Guardians.AnyAsync(guardian => guardian.Id == guardianId))
            throw new NotFoundException("Veli bulunamadı.");

        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherId is null) return null;

        var ownStudentIds = db.Enrollments
            .Where(enrollment => enrollment.TeacherId == teacherId && enrollment.Status == Domain.EnrollmentStatus.Active)
            .Select(enrollment => enrollment.StudentId);

        var linked = await db.StudentGuardians.AnyAsync(link =>
            link.GuardianId == guardianId && ownStudentIds.Contains(link.StudentId));

        if (!linked) throw new ForbiddenException("Bu veli sizin öğrencilerinizden birine bağlı değil.");
        return teacherId;
    }

    // Öğretmen yalnızca KENDİ adına kayıt açabilir. URL'deki teacherId'ye güvenmek,
    // bir öğretmenin başka bir öğretmenin adına öğrenci eklemesine izin verirdi.
    public static async Task<Guid?> EnsureActsAsSelfAsync(
        Guid teacherId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var scopedTeacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (scopedTeacherId is null) return null;

        if (scopedTeacherId != teacherId)
            throw new ForbiddenException("Yalnızca kendi adınıza öğrenci ekleyebilirsiniz.");

        return scopedTeacherId;
    }
}
