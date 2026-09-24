using System.Security.Claims;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Modules.Scheduling.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// docs/07-api.md GET /api/students/{studentId}/makeup-credits, POST /api/makeup-credits/{id}/use
// (A2). "Kullanma" = yeni bir MAKEUP dersi planlamak - lesson_series_id yok (ERD notu).
public static class MakeupCredits
{
    public record CreditResponse(
        Guid Id, Guid StudentId, Guid SourceLessonId, MakeupCreditEarnedReason EarnedReason,
        DateTimeOffset EarnedAt, DateTimeOffset ExpiresAt, MakeupCreditStatus Status, Guid? UsedLessonId,
        DateTimeOffset SourceLessonStartAt);

    public record UseRequest(Guid TeacherId, Guid InstrumentId, DateTimeOffset StartAt, int DurationMinutes);
    public record UseResponse(Guid CreditId, Guid NewLessonId);

    public static void MapMakeupCredits(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/students/{studentId:guid}/makeup-credits", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapPost("/api/makeup-credits/{creditId:guid}/use", UseAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> ListAsync(Guid studentId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } teacherId)
        {
            var isAssigned = await db.Enrollments.AnyAsync(e => e.StudentId == studentId && e.TeacherId == teacherId);
            if (!isAssigned) throw new ForbiddenException("Bu öğrenci size atanmamış.");
        }

        var credits = await db.MakeupCredits
            .Where(c => c.StudentId == studentId)
            .Join(db.Lessons,
                credit => credit.SourceLessonId,
                lesson => lesson.Id,
                (credit, lesson) => new { Credit = credit, SourceLessonStartAt = lesson.StartAt })
            .OrderByDescending(item => item.Credit.EarnedAt)
            .Select(item => new CreditResponse(
                item.Credit.Id,
                item.Credit.StudentId,
                item.Credit.SourceLessonId,
                item.Credit.EarnedReason,
                item.Credit.EarnedAt,
                item.Credit.ExpiresAt,
                item.Credit.Status,
                item.Credit.UsedLessonId,
                item.SourceLessonStartAt))
            .ToListAsync();

        return Results.Ok(credits);
    }

    private static async Task<IResult> UseAsync(
        Guid creditId, UseRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock, INotificationScheduler scheduler,
        IStaffNotifier staffNotifier)
    {
        if (request.DurationMinutes <= 0)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["durationMinutes"] = ["Süre pozitif olmalı."] });

        // Aynı telafi hakkına eş zamanlı gelen iki istek de AVAILABLE durumunu görüp iki
        // ayrı ders oluşturamasın. Satırı transaction sonuna kadar kilitleyerek yalnızca
        // ilk isteğin hakkı kullanmasına izin veriyoruz.
        await using var transaction = await db.Database.BeginTransactionAsync();
        var credit = await db.MakeupCredits
            .FromSqlInterpolated($"SELECT * FROM makeup_credits WHERE id = {creditId} FOR UPDATE")
            .SingleOrDefaultAsync()
            ?? throw new NotFoundException("Telafi kredisi bulunamadı.");

        if (credit.Status != MakeupCreditStatus.Available)
            throw new ConflictException("Bu telafi hakkı daha önce kullanılmış veya artık geçerli değil.");

        var sourceLesson = await db.Lessons.AsNoTracking()
            .SingleOrDefaultAsync(lesson => lesson.Id == credit.SourceLessonId)
            ?? throw new NotFoundException("İptal edilen kaynak ders bulunamadı.");

        var sourceLessonDate = DateOnly.FromDateTime(clock.ToSchoolLocal(sourceLesson.StartAt).Date);
        var requestedLessonDate = DateOnly.FromDateTime(clock.ToSchoolLocal(request.StartAt).Date);
        if (requestedLessonDate <= sourceLessonDate)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["startAt"] = ["Telafi dersi, iptal edilen dersin gününden sonraki bir güne planlanmalı."],
            });
        }

        if (request.StartAt <= clock.UtcNow)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["startAt"] = ["Telafi dersinin başlangıcı gelecekte olmalı."],
            });
        }

        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } scopedTeacherId && scopedTeacherId != request.TeacherId)
            throw new ForbiddenException("Telafi dersini yalnızca kendi öğretmen hesabınızla planlayabilirsiniz.");

        if (!await db.Teachers.AnyAsync(t => t.Id == request.TeacherId))
            throw new NotFoundException("Öğretmen bulunamadı.");
        if (!await db.Instruments.AnyAsync(i => i.Id == request.InstrumentId))
            throw new NotFoundException("Enstrüman bulunamadı.");

        var hasActiveEnrollment = await db.Enrollments.AnyAsync(enrollment =>
            enrollment.StudentId == credit.StudentId &&
            enrollment.TeacherId == request.TeacherId &&
            enrollment.InstrumentId == request.InstrumentId &&
            enrollment.Status == EnrollmentStatus.Active);
        if (!hasActiveEnrollment)
        {
            if (teacherScope is not null)
                throw new ForbiddenException("Bu öğrenci ve ders için aktif öğretmen kaydınız bulunmuyor.");

            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["teacherId"] = ["Öğrenci, öğretmen ve enstrüman arasında aktif bir ders kaydı bulunmalı."],
            });
        }

        var endAt = request.StartAt.AddMinutes(request.DurationMinutes);
        var hasConflict = await LessonConflictChecker.HasOverlapAsync(db, request.TeacherId, credit.StudentId, request.StartAt, endAt);
        if (hasConflict)
            throw new ConflictException("Bu saat, öğretmenin veya öğrencinin başka bir dersiyle çakışıyor.");

        var localLessonDate = DateOnly.FromDateTime(clock.ToSchoolLocal(request.StartAt).Date);
        var weekStart = StudentWeeklyLessonPolicy.StartOfWeek(localLessonDate);
        var weekEnd = weekStart.AddDays(7);
        var weekStartAt = LessonGenerator.ToUtcInstant(weekStart, TimeOnly.MinValue, clock.SchoolTimeZone);
        var weekEndAt = LessonGenerator.ToUtcInstant(weekEnd, TimeOnly.MinValue, clock.SchoolTimeZone);
        var weeklyLessonCount = await db.Lessons.CountAsync(lesson =>
            lesson.StudentId == credit.StudentId &&
            lesson.StartAt >= weekStartAt && lesson.StartAt < weekEndAt &&
            lesson.Status != LessonStatus.Cancelled && lesson.Status != LessonStatus.Rescheduled);
        if (weeklyLessonCount >= StudentWeeklyLessonPolicy.MaximumLessons)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["startAt"] = [$"Öğrencinin bu haftada zaten {StudentWeeklyLessonPolicy.MaximumLessons} dersi var. Başka bir hafta seçin."],
            });
        }

        var makeupLesson = Lesson.CreateMakeup(credit.StudentId, request.TeacherId, request.InstrumentId, request.StartAt, endAt, clock.UtcNow);
        db.Lessons.Add(makeupLesson);

        credit.Use(makeupLesson.Id, clock.UtcNow);

        var primaryGuardianId = await PrimaryGuardianResolver.ResolveAsync(db, credit.StudentId);
        if (primaryGuardianId is { } guardianId)
        {
            await scheduler.ScheduleAsync(NotificationJobType.MakeupApproved, "lesson", makeupLesson.Id, guardianId, clock.UtcNow);
        }

        await LessonChangeNotice.NotifyMakeupScheduledAsync(
            staffNotifier, db, clock, AuthContext.GetUserId(principal), request.TeacherId, credit.StudentId,
            makeupLesson.StartAt, makeupLesson.Id);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await staffNotifier.FlushEmailsAsync();
        return Results.Ok(new UseResponse(credit.Id, makeupLesson.Id));
    }
}
