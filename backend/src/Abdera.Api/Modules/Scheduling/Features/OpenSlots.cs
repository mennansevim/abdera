using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// GET /api/scheduling/open-slots - yeni bir veli geldiğinde "hangi öğretmen, hangi saatte boş"
// sorusunun yanıtı (Müsaitlik ekranı). Salt okunur, takvime dokunmaz; audit yazmaz.
//
// Hesap OpenSlotFinder'da: uygunluk penceresi eksi aktif ders serileri. Uygunluk tanımlamamış
// öğretmen listeye girmez - ders oluşturma onu her saat açık sayar (EnsureWithinAvailabilityAsync),
// ama bu ekranda her hücreyi şişirirdi; adı ayrıca döner ki ekran "tanımlı değil" diyebilsin.
public static class OpenSlots
{
    // FirstOpenDate'in ileriye bakma ufku: izin/tatil/telafi bir saati bundan uzun süre
    // kapatıyorsa o saat yeni kayıt için zaten pratik bir seçenek değil.
    private const int LookAheadWeeks = 8;
    private const int MinDurationMinutes = 15;
    private const int MaxDurationMinutes = 180;

    public record OpenSlotResponse(DayOfWeek DayOfWeek, TimeOnly StartTime, DateOnly? FirstOpenDate);
    public record TimeOffResponse(DateOnly StartsOn, DateOnly EndsOn, string? Reason);

    public record TeacherOpenSlotsResponse(
        Guid TeacherId, string TeacherName, IReadOnlyList<Guid> InstrumentIds,
        int WeeklyLessonCount, int WeeklyAvailableMinutes, int WeeklyBookedMinutes,
        IReadOnlyList<TimeOffResponse> UpcomingTimeOff, IReadOnlyList<OpenSlotResponse> Slots);

    public record TeacherRef(Guid TeacherId, string TeacherName);

    public record Response(
        DateOnly From, int DurationMinutes,
        IReadOnlyList<TeacherOpenSlotsResponse> Teachers,
        IReadOnlyList<TeacherRef> TeachersWithoutAvailability);

    public static void MapOpenSlots(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/scheduling/open-slots", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> GetAsync(
        Guid? instrumentId, int? durationMinutes, DateOnly? from, AbderaDbContext db, IClock clock)
    {
        var duration = durationMinutes ?? 45;
        if (duration is < MinDurationMinutes or > MaxDurationMinutes)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["durationMinutes"] = [$"Ders süresi {MinDurationMinutes}-{MaxDurationMinutes} dakika arasında olmalı."],
            });
        }

        var localNow = clock.ToSchoolLocal(clock.UtcNow).DateTime;
        var today = DateOnly.FromDateTime(localNow);
        var start = from ?? today;
        TimeOnly? notBeforeOnStart = start == today ? TimeOnly.FromDateTime(localNow) : null;
        var horizon = start.AddDays(7 * LookAheadWeeks);

        var teacherQuery = db.Teachers.Where(t => t.Status == TeacherStatus.Active);
        if (instrumentId is { } filterInstrumentId)
        {
            teacherQuery = teacherQuery.Where(t =>
                db.TeacherInstruments.Any(ti => ti.TeacherId == t.Id && ti.InstrumentId == filterInstrumentId));
        }

        var teachers = await teacherQuery
            .OrderBy(t => t.FirstName).ThenBy(t => t.LastName)
            .Select(t => new { t.Id, Name = t.FirstName + " " + t.LastName })
            .ToListAsync();
        var teacherIds = teachers.Select(t => t.Id).ToList();

        var instruments = await db.TeacherInstruments
            .Where(ti => teacherIds.Contains(ti.TeacherId))
            .Select(ti => new { ti.TeacherId, ti.InstrumentId })
            .ToListAsync();

        var windows = await db.TeacherAvailabilities
            .Where(a => teacherIds.Contains(a.TeacherId))
            .ToListAsync();

        // Bitişi `start`tan önce olan seri artık saati tutmuyor; ileri tarihte başlayacak bir
        // seri ise tutuyor - yeni kayıt da süresiz olduğu için o saate ileride çakışırdı.
        var series = await db.LessonSeries
            .Where(s => s.Status == LessonSeriesStatus.Active)
            .Where(s => s.EffectiveUntil == null || s.EffectiveUntil >= start)
            .Join(db.Enrollments, s => s.EnrollmentId, e => e.Id, (s, e) => new { Series = s, e.TeacherId })
            .Where(x => teacherIds.Contains(x.TeacherId))
            .ToListAsync();

        var fromInstant = LessonGenerator.ToUtcInstant(start, TimeOnly.MinValue, clock.SchoolTimeZone);
        var toInstant = LessonGenerator.ToUtcInstant(horizon, TimeOnly.MinValue, clock.SchoolTimeZone);
        var lessons = await db.Lessons
            .Where(l => teacherIds.Contains(l.TeacherId))
            .Where(l => l.Status != LessonStatus.Cancelled && l.Status != LessonStatus.Rescheduled)
            .Where(l => l.StartAt >= fromInstant && l.StartAt < toInstant)
            .Select(l => new { l.TeacherId, l.StartAt, l.EndAt })
            .ToListAsync();

        var timeOffs = await db.TeacherTimeOffs
            .Where(t => teacherIds.Contains(t.TeacherId) && t.EndsOn >= start && t.StartsOn < horizon)
            .OrderBy(t => t.StartsOn)
            .ToListAsync();

        var holidays = (await db.SchoolCalendarDays
                .Where(d => d.Type == SchoolCalendarDayType.Holiday && d.Date >= start && d.Date < horizon)
                .Select(d => d.Date)
                .ToListAsync())
            .ToHashSet();

        var result = new List<TeacherOpenSlotsResponse>();
        var withoutAvailability = new List<TeacherRef>();

        foreach (var teacher in teachers)
        {
            var teacherWindows = windows.Where(w => w.TeacherId == teacher.Id)
                .Select(w => new OpenSlotFinder.Window(w.DayOfWeek, w.StartTime, w.EndTime))
                .ToList();
            if (teacherWindows.Count == 0)
            {
                withoutAvailability.Add(new TeacherRef(teacher.Id, teacher.Name));
                continue;
            }

            var teacherSeries = series.Where(x => x.TeacherId == teacher.Id).Select(x => x.Series).ToList();
            var busy = teacherSeries
                .Select(s => new OpenSlotFinder.Busy(s.DayOfWeek, s.StartTime, s.DurationMinutes))
                .ToList();

            var oneOffLessons = lessons.Where(l => l.TeacherId == teacher.Id)
                .Select(l =>
                {
                    var localStart = clock.ToSchoolLocal(l.StartAt);
                    var localEnd = clock.ToSchoolLocal(l.EndAt);
                    return (DateOnly.FromDateTime(localStart.DateTime),
                        TimeOnly.FromDateTime(localStart.DateTime), TimeOnly.FromDateTime(localEnd.DateTime));
                })
                .ToList();

            var teacherTimeOffs = timeOffs.Where(t => t.TeacherId == teacher.Id).ToList();
            bool IsDayBlocked(DateOnly date) =>
                holidays.Contains(date) || teacherTimeOffs.Any(t => t.Covers(date));

            var slots = OpenSlotFinder.WeeklyFreeStarts(teacherWindows, busy, duration)
                .Select(slot => new OpenSlotResponse(slot.DayOfWeek, slot.StartTime,
                    OpenSlotFinder.FirstOpenDate(slot, duration, start, LookAheadWeeks, IsDayBlocked, oneOffLessons, notBeforeOnStart)))
                .ToList();

            result.Add(new TeacherOpenSlotsResponse(
                teacher.Id,
                teacher.Name,
                instruments.Where(i => i.TeacherId == teacher.Id).Select(i => i.InstrumentId).ToList(),
                teacherSeries.Count,
                teacherWindows.Sum(w => (int)(w.EndTime - w.StartTime).TotalMinutes),
                teacherSeries.Sum(s => s.DurationMinutes),
                teacherTimeOffs.Select(t => new TimeOffResponse(t.StartsOn, t.EndsOn, t.Reason)).ToList(),
                slots));
        }

        return Results.Ok(new Response(start, duration, result, withoutAvailability));
    }
}
