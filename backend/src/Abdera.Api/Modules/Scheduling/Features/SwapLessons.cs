using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// İki birebir dersin yer değiştirmesi - kullanıcı isteği: "Perşembe 15 Ferit'i Cuma 15'e,
// Cuma 15 Emir Ali'yi Perşembe 15'e alacağız." Tek tek taşımak mümkün değildi: ilk taşıma
// karşı dersin hâlâ orada durması yüzünden çakışma diye reddediliyordu.
//
// Her öğrenci kendi dersini (öğretmen, enstrüman, süre) korur; yalnızca başlangıç zamanları
// takas edilir. İki kapsam:
//   Once      - yalnızca bu iki ders (ders detayındaki düzenlemeyle aynı "eski satır
//               Rescheduled, yeni kopya" deseni - UpdateLesson).
//   Recurring - iki dersin haftalık programı, iki dersten erken olanının gününden itibaren
//               (program taşımayla aynı "eskisini kapat, yenisini aç" deseni - LessonSeriesFeatures).
// Her iki yolda da iki taraf önce birlikte doğrulanır, sonra tek transaction'da yazılır: biri
// taşınıp diğeri reddedilirse yarım kalmış bir takas kalmasın.
public static class SwapLessons
{
    public enum SwapScope { Once, Recurring }

    public record Request(Guid OtherLessonId, SwapScope Scope);

    // Once: yeni ders id'leri; Recurring: yeni seri id'leri. İlk alan URL'deki derse aittir.
    public record Response(SwapScope Scope, Guid FirstId, Guid SecondId);

    public static void MapSwapLessons(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lessons/{lessonId:guid}/swap", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
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
        if (request.OtherLessonId == lessonId)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["otherLessonId"] = ["Bir ders kendisiyle yer değiştiremez."],
            });

        // UpdateLesson ile aynı gerekçe: Lesson'da concurrency kolonu yok, iki satır da
        // pesimist kilitle tutulur. Kilit sırası id'ye göre sabit - aynı ikiliyi ters yönden
        // takas eden eş zamanlı iki istek birbirini kilitlemesin (deadlock).
        await using var transaction = await db.Database.BeginTransactionAsync();

        var otherLessonId = request.OtherLessonId;
        var locked = await db.Lessons
            .FromSqlInterpolated($"SELECT * FROM lessons WHERE id = {lessonId} OR id = {otherLessonId} ORDER BY id FOR UPDATE")
            .ToListAsync();
        var first = locked.SingleOrDefault(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");
        var second = locked.SingleOrDefault(l => l.Id == otherLessonId)
            ?? throw new NotFoundException("Yer değiştirilecek ders bulunamadı.");

        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } scopedTeacherId &&
            (first.TeacherId != scopedTeacherId || second.TeacherId != scopedTeacherId))
            throw new ForbiddenException("Öğretmen yalnızca kendi iki dersini yer değiştirebilir.");

        foreach (var lesson in new[] { first, second })
        {
            // Telafi dersinin saati telafi hakkının penceresine bağlı, grup üyesinin saati
            // grubuna - ikisi de takasa uygun değil, kendi ekranlarından taşınır.
            if (lesson.Status == LessonStatus.Makeup)
                throw new ConflictException("Telafi dersi yer değiştiremez; ders detayından taşıyın.");
            if (lesson.Status != LessonStatus.Normal)
                throw new ConflictException($"'{lesson.Status}' durumundaki bir ders yer değiştiremez.");
            if (lesson.StartAt <= clock.UtcNow && !AuthContext.IsAdmin(principal))
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["otherLessonId"] = ["Başlamış ya da geçmiş bir ders yer değiştiremez."],
                });
        }

        var seriesIds = new[] { first.LessonSeriesId, second.LessonSeriesId }.OfType<Guid>().ToList();
        if (await db.LessonSeries.AnyAsync(s => seriesIds.Contains(s.Id) && s.LessonGroupId != null))
            throw new ConflictException("Grup dersi yer değiştiremez; saati Grup dersleri ekranından değişir.");

        var actorId = AuthContext.GetUserId(principal);
        var result = request.Scope == SwapScope.Recurring
            ? await SwapSeriesAsync(first, second, actorId, db, clock, config, scheduler, staffNotifier)
            : await SwapOnceAsync(first, second, actorId, db, clock, scheduler, staffNotifier);

        await transaction.CommitAsync();
        await staffNotifier.FlushEmailsAsync();
        return Results.Ok(result);
    }

    private static async Task<Response> SwapOnceAsync(
        Lesson first, Lesson second, Guid? actorId, AbderaDbContext db, IClock clock,
        INotificationScheduler scheduler, IStaffNotifier staffNotifier)
    {
        var firstNewStart = second.StartAt;
        var firstNewEnd = firstNewStart + (first.EndAt - first.StartAt);
        var secondNewStart = first.StartAt;
        var secondNewEnd = secondNewStart + (second.EndAt - second.StartAt);

        await EnsureLessonSlotFreeAsync(db, first, firstNewStart, firstNewEnd, second.Id);
        await EnsureLessonSlotFreeAsync(db, second, secondNewStart, secondNewEnd, first.Id);
        // Süreler farklıysa iki yeni saat birbirine taşabilir (60 dk'lık ders 40 dk'lık dersin
        // yerine geçip bir sonrakine sarkar) - DB'deki kontrol bunu göremez, ikisi de henüz yok.
        var shareParticipant = first.TeacherId == second.TeacherId || first.StudentId == second.StudentId;
        if (shareParticipant && firstNewStart < secondNewEnd && secondNewStart < firstNewEnd)
            throw new ConflictException("Ders süreleri farklı olduğu için yer değiştirince iki ders birbiriyle çakışıyor.");

        var firstBefore = SerializeLesson(first);
        var secondBefore = SerializeLesson(second);

        var firstReplacement = Lesson.CreateEditedCopy(
            first, first.StudentId, first.TeacherId, firstNewStart, firstNewEnd, clock.UtcNow);
        var secondReplacement = Lesson.CreateEditedCopy(
            second, second.StudentId, second.TeacherId, secondNewStart, secondNewEnd, clock.UtcNow);
        db.Lessons.AddRange(firstReplacement, secondReplacement);

        var settings = await NotificationAutomationSettings.GetCurrentAsync(db);
        foreach (var (original, replacement, partner) in new[]
                 {
                     (first, firstReplacement, second),
                     (second, secondReplacement, first),
                 })
        {
            // CLAUDE.md "ders değişince eski job iptali": eski saatin hatırlatması iptal,
            // yenisi yeni saate kurulur; veliye "dersin saati değişti" mesajı.
            await scheduler.CancelPendingAsync("lesson", original.Id);
            var guardianId = await PrimaryGuardianResolver.ResolveAsync(db, replacement.StudentId);
            if (guardianId is { } id)
            {
                await scheduler.ScheduleAsync(
                    NotificationJobType.LessonReminder, "lesson", replacement.Id, id,
                    replacement.StartAt.AddMinutes(-settings.LessonReminderMinutesBefore));
                await scheduler.ScheduleAsync(
                    NotificationJobType.LessonRescheduled, "lesson", replacement.Id, id, clock.UtcNow);
            }

            var partnerName = await LessonChangeNotice.StudentNameAsync(db, partner.StudentId);
            await LessonChangeNotice.NotifyMovedAsync(
                staffNotifier, db, clock, actorId, replacement.TeacherId, replacement.StudentId,
                original.StartAt, replacement.StartAt, replacement.Id,
                extraNote: $"{partnerName} ile yer değiştirdi");

            db.AuditLogs.Add(AuditLog.Record(
                actorId,
                "lesson.swapped",
                nameof(Lesson),
                replacement.Id,
                clock.UtcNow,
                original == first ? firstBefore : secondBefore,
                JsonSerializer.Serialize(new
                {
                    replacement.StudentId,
                    replacement.TeacherId,
                    replacement.StartAt,
                    replacement.EndAt,
                    Status = replacement.Status.ToString(),
                    ReplacedLessonId = original.Id,
                    SwappedWithLessonId = partner.Id,
                })));
        }

        await db.SaveChangesAsync();
        return new Response(SwapScope.Once, firstReplacement.Id, secondReplacement.Id);
    }

    private static async Task<Response> SwapSeriesAsync(
        Lesson firstLesson, Lesson secondLesson, Guid? actorId, AbderaDbContext db, IClock clock,
        IConfiguration config, INotificationScheduler scheduler, IStaffNotifier staffNotifier)
    {
        if (firstLesson.LessonSeriesId is not { } firstSeriesId || secondLesson.LessonSeriesId is not { } secondSeriesId)
            throw new ConflictException("Haftalık programa bağlı olmayan bir ders için kalıcı yer değiştirme yapılamaz; yalnızca bu hafta seçeneğini kullanın.");
        if (firstSeriesId == secondSeriesId)
            throw new ConflictException("İki ders aynı haftalık programa ait.");

        var seriesRows = await db.LessonSeries
            .FromSqlInterpolated($"SELECT * FROM lesson_series WHERE id = {firstSeriesId} OR id = {secondSeriesId} ORDER BY id FOR UPDATE")
            .ToListAsync();
        var first = seriesRows.Single(s => s.Id == firstSeriesId);
        var second = seriesRows.Single(s => s.Id == secondSeriesId);
        if (first.Status != LessonSeriesStatus.Active || second.Status != LessonSeriesStatus.Active)
            throw new ConflictException("Sonlanmış bir ders programı yer değiştiremez.");

        var firstEnrollment = await db.Enrollments.SingleAsync(e => e.Id == first.EnrollmentId);
        var secondEnrollment = await db.Enrollments.SingleAsync(e => e.Id == second.EnrollmentId);

        // Takas, iki dersten erken olanının gününden geçerli: Perşembe ile Cuma takas
        // ediliyorsa o haftanın Perşembesi yeni programla başlar. Geçmişe dönük program
        // yazılmaz - geçmiş dersler hangi programdan doğduklarını korur.
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var earliestLessonDay = DateOnly.FromDateTime(clock.ToSchoolLocal(
            firstLesson.StartAt < secondLesson.StartAt ? firstLesson.StartAt : secondLesson.StartAt).Date);
        var effectiveFrom = earliestLessonDay > today ? earliestLessonDay : today;
        foreach (var series in new[] { first, second })
        {
            if (series.EffectiveUntil is { } until && effectiveFrom > until)
                throw new ConflictException("Ders programlarından biri bu tarihten önce bitiyor.");
        }

        // Her öğrenci kendi süresini korur, karşı programın gün ve başlangıç saatine geçer.
        var firstEnd = second.StartTime.AddMinutes(first.DurationMinutes);
        var secondEnd = first.StartTime.AddMinutes(second.DurationMinutes);

        await LessonSeriesFeatures.EnsureWithinAvailabilityAsync(
            firstEnrollment.TeacherId, second.DayOfWeek, second.StartTime, firstEnd, db);
        await LessonSeriesFeatures.EnsureWithinAvailabilityAsync(
            secondEnrollment.TeacherId, first.DayOfWeek, first.StartTime, secondEnd, db);
        await LessonSeriesFeatures.EnsureNoConflictAsync(
            firstEnrollment, second.DayOfWeek, second.StartTime, firstEnd, effectiveFrom, first.EffectiveUntil, db,
            excludeSeriesId: first.Id, swapPartnerSeriesId: second.Id);
        await LessonSeriesFeatures.EnsureNoConflictAsync(
            secondEnrollment, first.DayOfWeek, first.StartTime, secondEnd, effectiveFrom, second.EffectiveUntil, db,
            excludeSeriesId: second.Id, swapPartnerSeriesId: first.Id);

        var shareParticipant = firstEnrollment.TeacherId == secondEnrollment.TeacherId ||
                               firstEnrollment.StudentId == secondEnrollment.StudentId;
        if (shareParticipant && first.DayOfWeek == second.DayOfWeek &&
            second.StartTime < secondEnd && first.StartTime < firstEnd)
            throw new ConflictException("Ders süreleri farklı olduğu için yer değiştirince iki ders birbiriyle çakışıyor.");

        var firstBefore = LessonSeriesFeatures.SerializeSeries(first, firstEnrollment.TeacherId, firstEnrollment.StudentId);
        var secondBefore = LessonSeriesFeatures.SerializeSeries(second, secondEnrollment.TeacherId, secondEnrollment.StudentId);
        // ReplaceSeriesAsync eski seriyi kapatırken EffectiveUntil'i değiştirir; yeni seriye
        // devredilecek özgün bitiş tarihleri önceden alınır (RescheduleAsync ile aynı).
        var (firstDay, firstTime, firstUntil) = (first.DayOfWeek, first.StartTime, first.EffectiveUntil);
        var (secondDay, secondTime, secondUntil) = (second.DayOfWeek, second.StartTime, second.EffectiveUntil);

        var firstReplacement = await LessonSeriesFeatures.ReplaceSeriesAsync(
            first, secondDay, secondTime, first.DurationMinutes, effectiveFrom, firstUntil, db, clock, scheduler);
        var secondReplacement = await LessonSeriesFeatures.ReplaceSeriesAsync(
            second, firstDay, firstTime, second.DurationMinutes, effectiveFrom, secondUntil, db, clock, scheduler);

        foreach (var (original, replacement, enrollment, before, partnerEnrollment, previousDay, previousTime) in new[]
                 {
                     (first, firstReplacement, firstEnrollment, firstBefore, secondEnrollment, firstDay, firstTime),
                     (second, secondReplacement, secondEnrollment, secondBefore, firstEnrollment, secondDay, secondTime),
                 })
        {
            // Program taşımayla aynı: veliye tek "ders programı değişti" mesajı, öğretmen +
            // yöneticilere ekran içi bildirim (CLAUDE.md M4).
            await scheduler.CancelPendingAsync("lesson_series", original.Id);
            var guardianId = await PrimaryGuardianResolver.ResolveAsync(db, enrollment.StudentId);
            if (guardianId is { } id)
            {
                await scheduler.ScheduleAsync(
                    NotificationJobType.LessonScheduleChanged, "lesson_series", replacement.Id, id, clock.UtcNow);
            }
            await LessonChangeNotice.NotifyScheduleChangedAsync(
                staffNotifier, db, actorId, enrollment.TeacherId, enrollment.StudentId,
                previousDay, previousTime, replacement.DayOfWeek, replacement.StartTime, replacement.Id);

            db.AuditLogs.Add(AuditLog.Record(
                actorId,
                "lesson_series.swapped",
                nameof(LessonSeries),
                replacement.Id,
                clock.UtcNow,
                beforeJson: before,
                afterJson: JsonSerializer.Serialize(new
                {
                    replacement.EnrollmentId,
                    enrollment.TeacherId,
                    enrollment.StudentId,
                    DayOfWeek = replacement.DayOfWeek.ToString(),
                    StartTime = replacement.StartTime.ToString("HH:mm:ss"),
                    replacement.DurationMinutes,
                    replacement.EffectiveFrom,
                    replacement.EffectiveUntil,
                    ReplacedSeriesId = original.Id,
                    SwappedWithStudentId = partnerEnrollment.StudentId,
                })));
        }

        await db.SaveChangesAsync();

        await LessonSeriesFeatures.GenerateForSeriesAsync(firstReplacement, firstEnrollment, db, clock, config, scheduler);
        await LessonSeriesFeatures.GenerateForSeriesAsync(secondReplacement, secondEnrollment, db, clock, config, scheduler);

        return new Response(SwapScope.Recurring, firstReplacement.Id, secondReplacement.Id);
    }

    private static async Task EnsureLessonSlotFreeAsync(
        AbderaDbContext db, Lesson lesson, DateTimeOffset start, DateTimeOffset end, Guid partnerLessonId)
    {
        var hasConflict = await LessonConflictChecker.HasOverlapAsync(
            db, lesson.TeacherId, lesson.StudentId, start, end, lesson.Id, partnerLessonId);
        if (hasConflict)
            throw new ConflictException("Yeni saat, öğretmenin veya öğrencinin başka bir dersiyle çakışıyor.");
    }

    private static string SerializeLesson(Lesson lesson) =>
        JsonSerializer.Serialize(new
        {
            lesson.StudentId,
            lesson.TeacherId,
            lesson.StartAt,
            lesson.EndAt,
            Status = lesson.Status.ToString(),
        });
}
