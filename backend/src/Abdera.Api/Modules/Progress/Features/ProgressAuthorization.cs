using System.Security.Claims;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Features;

internal static class ProgressAuthorization
{
    public static async Task<Guid?> EnsureStudentAccessAsync(
        Guid studentId,
        ClaimsPrincipal principal,
        AbderaDbContext db)
    {
        if (!await db.Students.AnyAsync(student => student.Id == studentId))
            throw new NotFoundException("Öğrenci bulunamadı.");

        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherId is null) return null;

        if (!await db.Enrollments.AnyAsync(enrollment =>
                enrollment.StudentId == studentId && enrollment.TeacherId == teacherId))
        {
            throw new ForbiddenException("Bu öğrenci size atanmamış.");
        }

        return teacherId;
    }

    public static async Task<Guid?> EnsureLessonAccessAsync(
        Guid lessonId,
        ClaimsPrincipal principal,
        AbderaDbContext db)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(item => item.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");
        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherId is { } scopedTeacherId && scopedTeacherId != lesson.TeacherId)
            throw new ForbiddenException("Bu ders size atanmamış.");
        return teacherId;
    }
}
