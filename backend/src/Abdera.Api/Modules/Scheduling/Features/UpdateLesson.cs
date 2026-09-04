using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

public static class UpdateLesson
{
    public record Request(
        Guid StudentId,
        Guid TeacherId,
        DateTimeOffset StartAt,
        int DurationMinutes,
        LessonStatus Status);

    public record Response(Guid LessonId, Guid? ReplacedLessonId, LessonStatus Status);

    public static void MapUpdateLesson(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/lessons/{lessonId:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> HandleAsync(
        Guid lessonId,
        Request request,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock,
        IConfiguration config,
        INotificationScheduler scheduler,
        IStaffNotifier staffNotifier)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(item => item.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");

        if (lesson.Status != LessonStatus.Normal)
            throw new ConflictException($"'{lesson.Status}' durumundaki bir ders düzenlenemez.");
        if (request.Status is not LessonStatus.Normal and not LessonStatus.Cancelled)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["status"] = ["Ders detayı üzerinden yalnızca planlı veya iptal durumu seçilebilir."],
            });
        if (request.DurationMinutes is < Lesson.MinimumDurationMinutes or > Lesson.MaximumDurationMinutes)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["durationMinutes"] = [$"Ders süresi {Lesson.MinimumDurationMinutes}–{Lesson.MaximumDurationMinutes} dakika arasında olmalı."],
            });
        if (request.StartAt <= clock.UtcNow)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["startAt"] = ["Ders başlangıcı gelecekte olmalı."],
            });

        var actorId = AuthContext.GetUserId(principal);
        var before = JsonSerializer.Serialize(new
        {
            lesson.StudentId,
            lesson.TeacherId,
            lesson.StartAt,
            lesson.EndAt,
            Status = lesson.Status.ToString(),
        });

        if (request.Status == LessonStatus.Cancelled)
        {
            lesson.Cancel(clock.UtcNow);
            await scheduler.CancelPendingAsync("lesson", lesson.Id);

            if (!await db.MakeupCredits.AnyAsync(credit => credit.SourceLessonId == lesson.Id))
            {
                var validDays = config.GetValue("Policy:MakeupCreditValidDays", 60);
                db.MakeupCredits.Add(MakeupCredit.Earn(
                    lesson.StudentId,
                    lesson.Id,
                    MakeupCreditEarnedReason.SchoolCancelled,
                    clock.UtcNow,
                    validDays));
            }

            db.AuditLogs.Add(AuditLog.Record(
                actorId,
                "lesson.cancelled_from_detail",
                nameof(Lesson),
                lesson.Id,
                clock.UtcNow,
                before,
                JsonSerializer.Serialize(new { Status = lesson.Status.ToString() })));
            await db.SaveChangesAsync();
            return Results.Ok(new Response(lesson.Id, null, lesson.Status));
        }

        var enrollmentExists = await db.Enrollments.AnyAsync(enrollment =>
            enrollment.StudentId == request.StudentId &&
            enrollment.TeacherId == request.TeacherId &&
            enrollment.InstrumentId == lesson.InstrumentId &&
            enrollment.Status == EnrollmentStatus.Active);
        if (!enrollmentExists)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["studentId"] = ["Seçilen öğrenci, öğretmen ve enstrüman için aktif bir kurs kaydı bulunamadı."],
            });

        var endAt = request.StartAt.AddMinutes(request.DurationMinutes);
        var hasConflict = await LessonConflictChecker.HasOverlapAsync(
            db,
            request.TeacherId,
            request.StudentId,
            request.StartAt,
            endAt,
            lesson.Id);
        if (hasConflict)
            throw new ConflictException("Seçilen saat, öğretmenin veya öğrencinin başka bir dersiyle çakışıyor.");

        var replacement = Lesson.CreateEditedCopy(
            lesson,
            request.StudentId,
            request.TeacherId,
            request.StartAt,
            endAt,
            clock.UtcNow);
        db.Lessons.Add(replacement);

        await scheduler.CancelPendingAsync("lesson", lesson.Id);
        var primaryGuardianId = await PrimaryGuardianResolver.ResolveAsync(db, replacement.StudentId);
        if (primaryGuardianId is { } guardianId)
        {
            var settings = await NotificationAutomationSettings.GetCurrentAsync(db);
            await scheduler.ScheduleAsync(
                NotificationJobType.LessonReminder,
                "lesson",
                replacement.Id,
                guardianId,
                replacement.StartAt.AddMinutes(-settings.LessonReminderMinutesBefore));
            await scheduler.ScheduleAsync(
                NotificationJobType.LessonRescheduled,
                "lesson",
                replacement.Id,
                guardianId,
                clock.UtcNow);
        }

        // Ders detayından yapılan düzenleme de bir "taşıma"dır: saat değiştiyse dersi
        // programında taşıyan öğretmen(ler) bunu ekranında görmeli. Öğretmen de değiştiyse
        // dersi kaybeden taraf sessiz kalmasın diye eski öğretmene de düşer.
        if (replacement.StartAt != lesson.StartAt)
        {
            await LessonMovedNotice.NotifyTeacherAsync(
                staffNotifier, db, clock, replacement.TeacherId, replacement.StudentId,
                lesson.StartAt, replacement.StartAt, replacement.Id);

            if (replacement.TeacherId != lesson.TeacherId)
            {
                await LessonMovedNotice.NotifyTeacherAsync(
                    staffNotifier, db, clock, lesson.TeacherId, replacement.StudentId,
                    lesson.StartAt, replacement.StartAt, replacement.Id,
                    extraNote: "ders başka bir öğretmene aktarıldı");
            }
        }

        db.AuditLogs.Add(AuditLog.Record(
            actorId,
            "lesson.updated",
            nameof(Lesson),
            replacement.Id,
            clock.UtcNow,
            before,
            JsonSerializer.Serialize(new
            {
                replacement.StudentId,
                replacement.TeacherId,
                replacement.StartAt,
                replacement.EndAt,
                Status = replacement.Status.ToString(),
                ReplacedLessonId = lesson.Id,
            })));

        await db.SaveChangesAsync();
        return Results.Ok(new Response(replacement.Id, lesson.Id, replacement.Status));
    }
}
