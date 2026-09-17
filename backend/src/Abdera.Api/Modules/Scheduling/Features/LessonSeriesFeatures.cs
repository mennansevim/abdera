using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
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
//
// Öğretmen kendi ders programını girer (docs/10-decisions.md K2): seriyi yalnızca KENDİ
// kurs kaydı (enrollment) üzerinden açar/kapatır, admin okulun tamamını yönetir. Kapsam
// kontrolü serinin enrollment'ındaki teacherId üzerinden yapılır - istekteki id'ye değil.
//
// Ders serisi takvimi değiştirdiği için her oluşturma/bitirme audit_log'a yazılır (CLAUDE.md
// "para, takvim ve rıza değiştiren her use-case"); artık aktörün admin olduğu garanti
// olmadığından "kim" sorusunun yanıtı kritik.
public static class LessonSeriesFeatures
{
    public record CreateRequest(
        Guid EnrollmentId, DayOfWeek DayOfWeek, TimeOnly StartTime, int DurationMinutes,
        DateOnly EffectiveFrom, DateOnly? EffectiveUntil);

    public record EndRequest(DateOnly EffectiveUntil);

    // "Her hafta Pazartesi 18:00" satırını yeni bir gün/saate taşır. EffectiveFrom boşsa
    // değişiklik bugünden itibaren geçerli olur.
    public record RescheduleRequest(
        DayOfWeek DayOfWeek, TimeOnly StartTime, int DurationMinutes, DateOnly? EffectiveFrom);

    public record LessonSeriesResponse(
        Guid Id, Guid EnrollmentId, DayOfWeek DayOfWeek, TimeOnly StartTime, int DurationMinutes,
        DateOnly EffectiveFrom, DateOnly? EffectiveUntil, LessonSeriesStatus Status);

    // Öğrenci künyesindeki "Ders programı" satırı: seriyi tek başına göstermek yetmez,
    // hangi kurs/öğretmen olduğunu da aynı yanıtta veriyoruz - istemci ayrıca öğretmen ve
    // enstrüman listesi çekmek zorunda kalmasın.
    public record StudentSeriesResponse(
        Guid Id, Guid EnrollmentId, Guid TeacherId, string TeacherName,
        Guid InstrumentId, string InstrumentName, DayOfWeek DayOfWeek, TimeOnly StartTime,
        int DurationMinutes, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, LessonSeriesStatus Status);

    public record GenerationSummary(int Created, IReadOnlyList<DateOnly> SkippedHolidays, IReadOnlyList<DateOnly> SkippedTeacherTimeOff);
    public record CreateResponse(LessonSeriesResponse Series, GenerationSummary Generation);

    public static void MapLessonSeries(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/lesson-series").RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        group.MapPost("", CreateAsync);
        group.MapPatch("/{seriesId:guid}", EndAsync);
        group.MapPost("/{seriesId:guid}/generate", GenerateAsync);
        group.MapPost("/{seriesId:guid}/reschedule", RescheduleAsync);

        // Öğrenci künyesinde programı göstermek için - seriler öğrencinin kurs kayıtlarına
        // bağlı olduğundan iç içe kaynak olarak sunuluyor (Enrollments.cs ile aynı desen).
        app.MapGet("/api/students/{studentId:guid}/lesson-series", ListForStudentAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock,
        IConfiguration config, INotificationScheduler scheduler)
    {
        if (request.DurationMinutes <= 0)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["durationMinutes"] = ["Ders süresi pozitif olmalı."] });
        if (request.EffectiveUntil is { } until && until < request.EffectiveFrom)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["effectiveUntil"] = ["Bitiş tarihi başlangıçtan önce olamaz."] });

        var enrollment = await db.Enrollments.SingleOrDefaultAsync(e => e.Id == request.EnrollmentId)
            ?? throw new NotFoundException("Kayıt (enrollment) bulunamadı.");
        if (enrollment.Status != EnrollmentStatus.Active)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["enrollmentId"] = ["Bu kayıt aktif değil."] });

        await SchedulingAuthorization.EnsureActsAsSelfAsync(enrollment.TeacherId, principal, db);

        var endTime = request.StartTime.AddMinutes(request.DurationMinutes);

        await EnsureStudentWeeklyLimitAsync(
            enrollment.StudentId, request.EffectiveFrom, request.EffectiveUntil, db);
        await EnsureSingleSeriesPerInstrumentAsync(
            enrollment.StudentId, enrollment.InstrumentId, request.EffectiveFrom, request.EffectiveUntil, db);
        await EnsureWithinAvailabilityAsync(enrollment.TeacherId, request.DayOfWeek, request.StartTime, endTime, db);
        await EnsureNoConflictAsync(enrollment, request.DayOfWeek, request.StartTime, endTime, request.EffectiveFrom, request.EffectiveUntil, db);

        var series = LessonSeries.Create(
            request.EnrollmentId, request.DayOfWeek, request.StartTime, request.DurationMinutes,
            request.EffectiveFrom, request.EffectiveUntil, clock.UtcNow);
        db.LessonSeries.Add(series);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "lesson_series.created",
            nameof(LessonSeries),
            series.Id,
            clock.UtcNow,
            afterJson: SerializeSeries(series, enrollment.TeacherId, enrollment.StudentId)));
        await db.SaveChangesAsync();

        var generation = await GenerateForSeriesAsync(series, enrollment, db, clock, config, scheduler);

        return Results.Created($"/api/lesson-series/{series.Id}", new CreateResponse(ToResponse(series), generation));
    }

    private static async Task<IResult> EndAsync(
        Guid seriesId, EndRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock,
        INotificationScheduler scheduler)
    {
        var series = await db.LessonSeries.SingleOrDefaultAsync(s => s.Id == seriesId)
            ?? throw new NotFoundException("Ders serisi bulunamadı.");
        var enrollment = await db.Enrollments.SingleAsync(e => e.Id == series.EnrollmentId);

        // Seriyi bitirmek bir "silme" değil, durum değişikliği (Status=Ended) - bu yüzden
        // öğretmene kapalı değil: kendi girdiği hatalı programı yönetici beklemeden
        // düzeltebilmeli. Yalnızca KENDİ serisi (docs/10-decisions.md K2).
        await SchedulingAuthorization.EnsureActsAsSelfAsync(enrollment.TeacherId, principal, db);

        var beforeJson = SerializeSeries(series, enrollment.TeacherId, enrollment.StudentId);
        series.EndAs(request.EffectiveUntil, clock.UtcNow);

        // Bitiş tarihinden sonraki, henüz gerçekleşmemiş (Normal) üretilmiş dersler kaldırılır -
        // seri kısaltıldığında gelecekteki ders üretimi de bu tarihe göre durmalı.
        var futureLessons = await db.Lessons
            .Where(l => l.LessonSeriesId == seriesId && l.Status == LessonStatus.Normal)
            .Where(l => DateOnly.FromDateTime(l.StartAt.UtcDateTime) > request.EffectiveUntil)
            .ToListAsync();
        db.Lessons.RemoveRange(futureLessons);
        // Silinen derslere kurulmuş bekleyen hatırlatmalar da iptal edilir (CLAUDE.md
        // "ders değişince eski job iptali") - job satırının lessons'a FK'sı yok, kendiliğinden
        // temizlenmez ve dispatcher var olmayan bir ders için mesaj kurmaya çalışırdı.
        foreach (var stale in futureLessons)
        {
            await scheduler.CancelPendingAsync("lesson", stale.Id);
        }

        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "lesson_series.ended",
            nameof(LessonSeries),
            series.Id,
            clock.UtcNow,
            beforeJson: beforeJson,
            afterJson: SerializeSeries(series, enrollment.TeacherId, enrollment.StudentId)));

        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(series));
    }


    // Öğrencinin künyesindeki program listesi. Teacher oturumunda yalnızca KENDİ kurs
    // kaydından doğan seriler görünür - bir öğretmen aynı öğrencinin başka bir öğretmenle
    // olan programını görmemeli (Enrollments.ListAsync ile aynı kural).
    private static async Task<IResult> ListForStudentAsync(
        Guid studentId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);

        var query = db.LessonSeries
            .Where(series => series.Status == LessonSeriesStatus.Active)
            .Join(db.Enrollments, series => series.EnrollmentId, e => e.Id, (series, e) => new { Series = series, Enrollment = e })
            .Where(x => x.Enrollment.StudentId == studentId);

        if (teacherScope is { } scopedTeacherId)
        {
            query = query.Where(x => x.Enrollment.TeacherId == scopedTeacherId);
        }

        // Not: OrderBy, StudentSeriesResponse'a (record) projeksiyondan ÖNCE - EF Core bir
        // record constructor alanına göre sıralamayı SQL'e çeviremiyor (CLAUDE.md).
        var rows = await query
            .Join(db.Teachers, x => x.Enrollment.TeacherId, t => t.Id, (x, t) => new { x.Series, x.Enrollment, Teacher = t })
            .Join(db.Instruments, x => x.Enrollment.InstrumentId, i => i.Id, (x, i) => new { x.Series, x.Enrollment, x.Teacher, Instrument = i })
            .OrderBy(x => x.Series.DayOfWeek).ThenBy(x => x.Series.StartTime)
            .Select(x => new StudentSeriesResponse(
                x.Series.Id, x.Series.EnrollmentId,
                x.Teacher.Id, x.Teacher.FirstName + " " + x.Teacher.LastName,
                x.Instrument.Id, x.Instrument.Name,
                x.Series.DayOfWeek, x.Series.StartTime, x.Series.DurationMinutes,
                x.Series.EffectiveFrom, x.Series.EffectiveUntil, x.Series.Status))
            .ToListAsync();

        return Results.Ok(rows);
    }

    // "Bu ders artık Pazartesi 18:00" - seriyi yerinde güncellemek yerine eskisini kapatıp
    // yenisini açıyoruz: tablo zaten EffectiveFrom/EffectiveUntil ile bunun için tasarlandı
    // ve geçmiş dersler hangi programdan doğduklarını korur. Yerinde güncelleme, geçmişe
    // dönük olarak "bu ders hep Pazartesi'ydi" yalanını söylerdi.
    private static async Task<IResult> RescheduleAsync(
        Guid seriesId, RescheduleRequest request, ClaimsPrincipal principal, AbderaDbContext db,
        IClock clock, IConfiguration config, INotificationScheduler scheduler)
    {
        if (request.DurationMinutes <= 0)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["durationMinutes"] = ["Ders süresi pozitif olmalı."] });

        var series = await db.LessonSeries.SingleOrDefaultAsync(s => s.Id == seriesId)
            ?? throw new NotFoundException("Ders serisi bulunamadı.");
        if (series.Status != LessonSeriesStatus.Active)
            throw new ConflictException("Sonlanmış bir ders programı taşınamaz; yeni bir program oluşturun.");

        var enrollment = await db.Enrollments.SingleAsync(e => e.Id == series.EnrollmentId);
        await SchedulingAuthorization.EnsureActsAsSelfAsync(enrollment.TeacherId, principal, db);

        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var effectiveFrom = request.EffectiveFrom ?? (series.EffectiveFrom > today ? series.EffectiveFrom : today);
        if (series.EffectiveUntil is { } untilCheck && effectiveFrom > untilCheck)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["effectiveFrom"] = ["Bu tarih programın bitiş tarihinden sonra."] });

        // EndAs aşağıda EffectiveUntil'i değiştiriyor; serinin ÖZGÜN bitiş tarihi yeni
        // seriye devredileceği için önceden alınır (aksi halde yeni seri "dün biten" bir
        // program olarak doğardı).
        var originalUntil = series.EffectiveUntil;

        var endTime = request.StartTime.AddMinutes(request.DurationMinutes);
        await EnsureWithinAvailabilityAsync(enrollment.TeacherId, request.DayOfWeek, request.StartTime, endTime, db);
        // Çakışma kontrolünden taşınan serinin KENDİSİ dışlanır - aksi halde her taşıma
        // "kendisiyle çakışıyor" diye reddedilirdi.
        await EnsureNoConflictAsync(
            enrollment, request.DayOfWeek, request.StartTime, endTime,
            effectiveFrom, originalUntil, db, excludeSeriesId: series.Id);
        await EnsureSingleSeriesPerInstrumentAsync(
            enrollment.StudentId, enrollment.InstrumentId, effectiveFrom, originalUntil, db,
            excludeSeriesId: series.Id);

        var beforeJson = SerializeSeries(series, enrollment.TeacherId, enrollment.StudentId);

        // Eskisi bir gün öncesinden kapanır. Program daha başlamadan taşınıyorsa (aynı gün
        // açılıp düzeltiliyor) geriye kapatılamaz - o zaman kendi başlangıç gününde kapanır
        // ve zaten üretilmiş dersleri aşağıda kaldırılır, yani hiç ders doğurmamış olur.
        var previousDay = effectiveFrom.AddDays(-1);
        series.EndAs(previousDay < series.EffectiveFrom ? series.EffectiveFrom : previousDay, clock.UtcNow);

        var staleLessons = await db.Lessons
            .Where(l => l.LessonSeriesId == series.Id && l.Status == LessonStatus.Normal)
            .Where(l => DateOnly.FromDateTime(l.StartAt.UtcDateTime) >= effectiveFrom)
            .ToListAsync();
        db.Lessons.RemoveRange(staleLessons);
        // Kaldırılan derslere kurulmuş bekleyen hatırlatmalar da iptal edilir - CLAUDE.md
        // "ders değişince eski job iptali" invariant'ı. Aksi halde veliye artık var olmayan
        // bir ders için hatırlatma giderdi.
        foreach (var stale in staleLessons)
        {
            await scheduler.CancelPendingAsync("lesson", stale.Id);
        }

        var replacement = LessonSeries.Create(
            series.EnrollmentId, request.DayOfWeek, request.StartTime, request.DurationMinutes,
            effectiveFrom, originalUntil, clock.UtcNow);
        db.LessonSeries.Add(replacement);

        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "lesson_series.rescheduled",
            nameof(LessonSeries),
            replacement.Id,
            clock.UtcNow,
            beforeJson: beforeJson,
            afterJson: SerializeSeries(replacement, enrollment.TeacherId, enrollment.StudentId)));

        await db.SaveChangesAsync();

        var generation = await GenerateForSeriesAsync(replacement, enrollment, db, clock, config, scheduler);
        return Results.Ok(new CreateResponse(ToResponse(replacement), generation));
    }

    private static async Task<IResult> GenerateAsync(
        Guid seriesId, ClaimsPrincipal principal, AbderaDbContext db, IClock clock, IConfiguration config,
        INotificationScheduler scheduler)
    {
        var series = await db.LessonSeries.SingleOrDefaultAsync(s => s.Id == seriesId)
            ?? throw new NotFoundException("Ders serisi bulunamadı.");
        var enrollment = await db.Enrollments.SingleAsync(e => e.Id == series.EnrollmentId);

        await SchedulingAuthorization.EnsureActsAsSelfAsync(enrollment.TeacherId, principal, db);

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

    // excludeSeriesId: bir seriyi yeni gün/saate taşırken serinin kendisi aday listesinden
    // çıkarılır, aksi halde her taşıma kendi eski kaydıyla çakışırdı.
    private static async Task EnsureNoConflictAsync(
        Enrollment enrollment, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime,
        DateOnly effectiveFrom, DateOnly? effectiveUntil, AbderaDbContext db, Guid? excludeSeriesId = null)
    {
        var candidates = await db.LessonSeries
            .Where(s => s.Status == LessonSeriesStatus.Active && s.DayOfWeek == dayOfWeek)
            .Where(s => excludeSeriesId == null || s.Id != excludeSeriesId)
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
                $"Bu saat aralığı {conflictType} başka bir ders serisiyle çakışıyor ({Describe(candidate.Series.DayOfWeek, candidate.Series.StartTime)}).");
        }
    }

    // Kullanıcı kuralı: "bir öğrenci aynı enstrüman için birden fazla ders alamasın, farklı
    // saatler de olsa." Kayıt (enrollment) seviyesindeki kısıt yalnızca aynı ÖĞRETMEN için
    // mükerrer kaydı engelliyordu; aynı enstrümanı iki ayrı öğretmenden almak ya da tek kayıt
    // üzerine ikinci bir haftalık program açmak hâlâ mümkündü - takvimde aynı öğrencinin
    // haftada birkaç kez aynı derste görünmesinin sebebi buydu.
    private static async Task EnsureSingleSeriesPerInstrumentAsync(
        Guid studentId, Guid instrumentId, DateOnly effectiveFrom, DateOnly? effectiveUntil,
        AbderaDbContext db, Guid? excludeSeriesId = null)
    {
        var sameInstrumentSeries = await db.LessonSeries
            .Where(series => series.Status == LessonSeriesStatus.Active)
            .Where(series => excludeSeriesId == null || series.Id != excludeSeriesId)
            .Join(db.Enrollments, series => series.EnrollmentId, e => e.Id, (series, e) => new { Series = series, Enrollment = e })
            .Where(x => x.Enrollment.StudentId == studentId && x.Enrollment.InstrumentId == instrumentId)
            .Select(x => x.Series)
            .ToListAsync();

        // Yalnızca tarih aralığı çakışanlar sayılır: eski program kapatılıp yenisi açıldığında
        // (ders saati değişikliği) bu kural engel olmamalı.
        var overlapping = sameInstrumentSeries.FirstOrDefault(series => DateRangesOverlap(
            effectiveFrom, effectiveUntil, series.EffectiveFrom, series.EffectiveUntil));

        if (overlapping is not null)
        {
            throw new ConflictException(
                "Bu öğrencinin bu enstrüman için zaten bir ders programı var " +
                $"({Describe(overlapping.DayOfWeek, overlapping.StartTime)}). " +
                "Yeni bir program açmak yerine mevcut programın saatini değiştirin.");
        }
    }

    private static async Task EnsureStudentWeeklyLimitAsync(
        Guid studentId, DateOnly effectiveFrom, DateOnly? effectiveUntil, AbderaDbContext db)
    {
        var existingSeries = await db.LessonSeries
            .Where(s => s.Status == LessonSeriesStatus.Active)
            .Join(db.Enrollments, s => s.EnrollmentId, e => e.Id, (series, enrollment) => new { Series = series, Enrollment = enrollment })
            .Where(x => x.Enrollment.StudentId == studentId)
            .Select(x => x.Series)
            .ToListAsync();

        var overlappingSeriesCount = existingSeries.Count(series => DateRangesOverlap(
            effectiveFrom, effectiveUntil, series.EffectiveFrom, series.EffectiveUntil));

        if (overlappingSeriesCount >= StudentWeeklyLessonPolicy.MaximumLessons)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["enrollmentId"] = [$"Bir öğrenci haftada en fazla {StudentWeeklyLessonPolicy.MaximumLessons} düzenli ders alabilir."],
            });
        }
    }

    private static bool DateRangesOverlap(DateOnly aStart, DateOnly? aEnd, DateOnly bStart, DateOnly? bEnd)
    {
        var aEndOrMax = aEnd ?? DateOnly.MaxValue;
        var bEndOrMax = bEnd ?? DateOnly.MaxValue;
        return aStart <= bEndOrMax && bStart <= aEndOrMax;
    }

    // Hata metinleri veliye/öğretmene görünür, yani Türkçe olmalı (CLAUDE.md dil kuralı).
    // CultureInfo yerine sabit dizi: adlandırılmış kültür çağrısı Alpine imajında
    // CultureNotFoundException riski taşıyor (CLAUDE.md globalization notu).
    private static readonly string[] DayNamesTr =
        ["Pazar", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi"];

    private static string Describe(DayOfWeek day, TimeOnly startTime) =>
        $"{DayNamesTr[(int)day]} {startTime:HH\\:mm}";

    // CLAUDE.md: jsonb kolonlarına yazılan metin ASLA string interpolation ile kurulmaz -
    // JsonSerializer hem kültürden bağımsız hem de kaçışı otomatik yapar.
    private static string SerializeSeries(LessonSeries series, Guid teacherId, Guid studentId) =>
        JsonSerializer.Serialize(new
        {
            series.EnrollmentId,
            TeacherId = teacherId,
            StudentId = studentId,
            DayOfWeek = series.DayOfWeek.ToString(),
            StartTime = series.StartTime.ToString("HH:mm:ss"),
            series.DurationMinutes,
            series.EffectiveFrom,
            series.EffectiveUntil,
            Status = series.Status.ToString(),
        });

    private static LessonSeriesResponse ToResponse(LessonSeries s) => new(
        s.Id, s.EnrollmentId, s.DayOfWeek, s.StartTime, s.DurationMinutes, s.EffectiveFrom, s.EffectiveUntil, s.Status);
}
