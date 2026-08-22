using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// docs/07-api.md POST/PATCH /api/lesson-series. Master prompt: "Validate teacher
// availability, student conflicts, teacher conflicts, valid duration, and valid time
// ranges... Generate concrete occurrences for a rolling window... idempotent."
public static class LessonSeriesFeatures
{
    public record CreateRequest(
        Guid EnrollmentId, DayOfWeek DayOfWeek, TimeOnly StartTime, int DurationMinutes,
        DateOnly EffectiveFrom, DateOnly? EffectiveUntil);

    public record EndRequest(DateOnly EffectiveUntil);

    public record LessonSeriesResponse(
        Guid Id, Guid EnrollmentId, DayOfWeek DayOfWeek, TimeOnly StartTime, int DurationMinutes,
        DateOnly EffectiveFrom, DateOnly? EffectiveUntil, LessonSeriesStatus Status);

    public record GenerationSummary(int Created, IReadOnlyList<DateOnly> SkippedHolidays, IReadOnlyList<DateOnly> SkippedTeacherTimeOff);
    public record CreateResponse(LessonSeriesResponse Series, GenerationSummary Generation);

    public static void MapLessonSeries(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/lesson-series").RequireAuthorization(AuthorizationPolicies.AdminOnly);

        group.MapPost("", CreateAsync);
        group.MapPatch("/{seriesId:guid}", EndAsync);
        group.MapPost("/{seriesId:guid}/generate", GenerateAsync);
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest request, AbderaDbContext db, IClock clock, IConfiguration config, INotificationScheduler scheduler)
    {
        if (request.DurationMinutes <= 0)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["durationMinutes"] = ["Ders süresi pozitif olmalı."] });
        if (request.EffectiveUntil is { } until && until < request.EffectiveFrom)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["effectiveUntil"] = ["Bitiş tarihi başlangıçtan önce olamaz."] });

        var enrollment = await db.Enrollments.SingleOrDefaultAsync(e => e.Id == request.EnrollmentId)
            ?? throw new NotFoundException("Kayıt (enrollment) bulunamadı.");
        if (enrollment.Status != EnrollmentStatus.Active)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["enrollmentId"] = ["Bu kayıt aktif değil."] });

        var endTime = request.StartTime.AddMinutes(request.DurationMinutes);

        await EnsureWithinAvailabilityAsync(enrollment.TeacherId, request.DayOfWeek, request.StartTime, endTime, db);
        await EnsureNoConflictAsync(enrollment, request.DayOfWeek, request.StartTime, endTime, request.EffectiveFrom, request.EffectiveUntil, db);

        var series = LessonSeries.Create(
            request.EnrollmentId, request.DayOfWeek, request.StartTime, request.DurationMinutes,
            request.EffectiveFrom, request.EffectiveUntil, clock.UtcNow);
        db.LessonSeries.Add(series);
        await db.SaveChangesAsync();

        var generation = await GenerateForSeriesAsync(series, enrollment, db, clock, config, scheduler);

        return Results.Created($"/api/lesson-series/{series.Id}", new CreateResponse(ToResponse(series), generation));
    }

    private static async Task<IResult> EndAsync(Guid seriesId, EndRequest request, AbderaDbContext db, IClock clock)
    {
        var series = await db.LessonSeries.SingleOrDefaultAsync(s => s.Id == seriesId)
            ?? throw new NotFoundException("Ders serisi bulunamadı.");

        series.EndAs(request.EffectiveUntil, clock.UtcNow);

        // Bitiş tarihinden sonraki, henüz gerçekleşmemiş (Normal) üretilmiş dersler kaldırılır -
        // seri kısaltıldığında gelecekteki ders üretimi de bu tarihe göre durmalı.
        var futureLessons = await db.Lessons
            .Where(l => l.LessonSeriesId == seriesId && l.Status == LessonStatus.Normal)
            .Where(l => DateOnly.FromDateTime(l.StartAt.UtcDateTime) > request.EffectiveUntil)
            .ToListAsync();
        db.Lessons.RemoveRange(futureLessons);

        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(series));
    }

    private static async Task<IResult> GenerateAsync(
        Guid seriesId, AbderaDbContext db, IClock clock, IConfiguration config, INotificationScheduler scheduler)
    {
        var series = await db.LessonSeries.SingleOrDefaultAsync(s => s.Id == seriesId)
            ?? throw new NotFoundException("Ders serisi bulunamadı.");
        var enrollment = await db.Enrollments.SingleAsync(e => e.Id == series.EnrollmentId);

        var generation = await GenerateForSeriesAsync(series, enrollment, db, clock, config, scheduler);
        return Results.Ok(generation);
    }

    private static async Task<GenerationSummary> GenerateForSeriesAsync(
        LessonSeries series, Enrollment enrollment, AbderaDbContext db, IClock clock, IConfiguration config,
        INotificationScheduler scheduler)
    {
        var weeks = config.GetValue("Scheduling:GenerationWeeks", 10);
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var windowStart = series.EffectiveFrom > today ? series.EffectiveFrom : today;
        var windowEnd = windowStart.AddDays(weeks * 7);

        var existingDates = (await db.Lessons
                .Where(l => l.LessonSeriesId == series.Id)
                .Select(l => l.StartAt)
                .ToListAsync())
            .Select(startAt => DateOnly.FromDateTime(clock.ToSchoolLocal(startAt).Date))
            .ToHashSet();

        var holidayDates = await db.SchoolCalendarDays
            .Where(d => d.Type == SchoolCalendarDayType.Holiday && d.Date >= windowStart && d.Date <= windowEnd)
            .Select(d => d.Date)
            .ToHashSetAsync();

        var timeOffRanges = await db.TeacherTimeOffs
            .Where(t => t.TeacherId == enrollment.TeacherId && t.EndsOn >= windowStart && t.StartsOn <= windowEnd)
            .Select(t => new ValueTuple<DateOnly, DateOnly>(t.StartsOn, t.EndsOn))
            .ToListAsync();

        var plan = LessonGenerator.Plan(series, windowStart, windowEnd, existingDates, holidayDates, timeOffRanges);

        // docs/06-whatsapp.md: her üretilen ders için dersten (admin panelden ayarlanabilir,
        // varsayılan 60 dk - bkz. NotificationAutomationSettings, Faz 3) önce bir LESSON_REMINDER
        // job'ı kurulur - yalnızca öğrencinin birincil velisine.
        var primaryGuardianId = await PrimaryGuardianResolver.ResolveAsync(db, enrollment.StudentId);
        var automationSettings = await NotificationAutomationSettings.GetCurrentAsync(db);
        var reminderMinutesBefore = automationSettings.LessonReminderMinutesBefore;

        foreach (var occurrence in plan.ToCreate)
        {
            var startAt = LessonGenerator.ToUtcInstant(occurrence.Date, occurrence.StartTime, clock.SchoolTimeZone);
            var endAt = LessonGenerator.ToUtcInstant(occurrence.Date, occurrence.EndTime, clock.SchoolTimeZone);

            var lesson = Lesson.CreateFromSeries(
                series.Id, enrollment.StudentId, enrollment.TeacherId, enrollment.InstrumentId,
                startAt, endAt, clock.UtcNow);
            db.Lessons.Add(lesson);

            if (primaryGuardianId is { } guardianId)
            {
                await scheduler.ScheduleAsync(
                    NotificationJobType.LessonReminder, "lesson", lesson.Id, guardianId,
                    startAt.AddMinutes(-reminderMinutesBefore));
            }
        }

        await db.SaveChangesAsync();

        return new GenerationSummary(plan.ToCreate.Count, plan.SkippedHolidays, plan.SkippedTeacherTimeOff);
    }

    private static async Task EnsureWithinAvailabilityAsync(
        Guid teacherId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime, AbderaDbContext db)
    {
        var availabilities = await db.TeacherAvailabilities.Where(a => a.TeacherId == teacherId).ToListAsync();
        // Öğretmen için hiç uygunluk tanımlanmamışsa kısıtlama uygulanmaz (opsiyonel alan) -
        // tanımlanmışsa en az bir pencere bu aralığı kapsamalı.
        if (availabilities.Count == 0) return;

        var covered = availabilities.Any(a => a.Covers(dayOfWeek, startTime, endTime));
        if (!covered)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["startTime"] = ["Bu saat aralığı öğretmenin tanımlı uygunluk pencerelerinin dışında."],
            });
        }
    }

    private static async Task EnsureNoConflictAsync(
        Enrollment enrollment, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime,
        DateOnly effectiveFrom, DateOnly? effectiveUntil, AbderaDbContext db)
    {
        var candidates = await db.LessonSeries
            .Where(s => s.Status == LessonSeriesStatus.Active && s.DayOfWeek == dayOfWeek)
            .Join(db.Enrollments, s => s.EnrollmentId, e => e.Id, (s, e) => new { Series = s, Enrollment = e })
            .Where(x => x.Enrollment.TeacherId == enrollment.TeacherId || x.Enrollment.StudentId == enrollment.StudentId)
            .ToListAsync();

        foreach (var candidate in candidates)
        {
            var candidateEnd = candidate.Series.StartTime.AddMinutes(candidate.Series.DurationMinutes);
            var timeOverlaps = startTime < candidateEnd && candidate.Series.StartTime < endTime;
            var dateRangeOverlaps = DateRangesOverlap(
                effectiveFrom, effectiveUntil, candidate.Series.EffectiveFrom, candidate.Series.EffectiveUntil);

            if (!timeOverlaps || !dateRangeOverlaps) continue;

            var conflictType = candidate.Enrollment.TeacherId == enrollment.TeacherId ? "öğretmenin" : "öğrencinin";
            throw new ConflictException(
                $"Bu saat aralığı {conflictType} başka bir ders serisiyle çakışıyor ({candidate.Series.DayOfWeek} {candidate.Series.StartTime}).");
        }
    }

    private static bool DateRangesOverlap(DateOnly aStart, DateOnly? aEnd, DateOnly bStart, DateOnly? bEnd)
    {
        var aEndOrMax = aEnd ?? DateOnly.MaxValue;
        var bEndOrMax = bEnd ?? DateOnly.MaxValue;
        return aStart <= bEndOrMax && bStart <= aEndOrMax;
    }

    private static LessonSeriesResponse ToResponse(LessonSeries s) => new(
        s.Id, s.EnrollmentId, s.DayOfWeek, s.StartTime, s.DurationMinutes, s.EffectiveFrom, s.EffectiveUntil, s.Status);
}
