using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Infrastructure;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// Yoklama ekranının yönetici görünümü (docs/10-decisions.md AD): her birebir kurs kaydının
// yoklama kartı - okulun kâğıt "Yoklama Listesi"nin dijital hali. Kart 4'er satırlık dönemlerden
// oluşur; Geldi ve Mazeretli satır doldurur, Gelmedi kartın altına not düşer. Her dönemin
// yanında o dönemin aidatı (ödendi/ödenmedi) durur.
//
// Kart her istekte yoklamadan yeniden kurulur; hangi dersin hangi döneme düştüğü tahakkuku yapan
// CycleReceivableAccruer ile aynı sayımdır (LoadCardLessonsAsync), ayrı bir kopya değil.
public static class AttendanceCards
{
    public enum CardState
    {
        // Geçiş tarihinden sonra henüz sayılan ders yok - ilk ders 1. dönemi açacak.
        NotStarted,
        InProgress,
        // Dönemin 4. satırına 1 ders kaldı.
        LastLesson,
        // 4 satır doldu, sonraki dönem ödenmemiş: bir sonraki derste yeni aidat tahakkuk edecek.
        AwaitingPayment,
        // 4 satır doldu ve sonraki dönem peşin ödenmiş.
        NextPrepaid,
        // Başlamış bir dönemin aidatı (tamamı ya da kalanı) ödenmemiş.
        Unpaid,
    }

    // EntryId doluysa satır karta elle yazılmış geçmiş derstir (AD10); düzenlenip silinebilir.
    public record CardLesson(Guid? LessonId, DateTimeOffset StartAt, DateOnly Date, AttendanceStatus Status, Guid? EntryId = null);

    public record CycleReceivable(
        Guid Id, ReceivableStatus Status, decimal Amount, decimal Paid, string Currency, DateOnly DueDate, DateOnly? AccruedOn);

    public record CardCycle(int Number, List<CardLesson> Filled, List<CardLesson> Misses, CycleReceivable? Receivable);

    public record Card(
        Guid EnrollmentId, Guid StudentId, string StudentName, Guid TeacherId, string TeacherName, string InstrumentName,
        // Dönem sayacının başladığı gün; null: kayda başlangıç girilmedi ve okul geneli geçiş kapalı.
        DateOnly StartedAt, DateOnly? CountingFrom, int CurrentCycle, int FilledInCurrent, CardState State,
        decimal UnpaidAmount, string Currency, DateOnly? LastLessonDate, List<CardCycle> Cycles,
        // Geçiş gününden önce işlenmiş dersler: yalnızca bilgi, karta sayılmaz ve aidat açmaz.
        List<CardLesson> EarlierLessons,
        // Sayaç başlangıcı kayda özel girilmiş mi (öğrencinin ilk giriş günü, AD9).
        bool HasOwnCycleStart = false);

    // EntriesFrom: karta elle ders yazılabilecek en eski gün (geçen ayın 1'i, AD11).
    public record CardsResponse(DateOnly? CycleBillingStartsOn, List<Card> Cards, DateOnly EntriesFrom = default);

    // Geriye dönük yazım penceresi: geçen ayın 1'inden bugüne. Okul geçen yıldan beri kâğıt tutuyor
    // ama yalnızca son bir ayı (son 4 dersi) karta geçirip sayacı oradan başlatıyor.
    public static DateOnly EntriesFrom(DateOnly today) => new DateOnly(today.Year, today.Month, 1).AddMonths(-1);

    public static void MapAttendanceCards(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/attendance/cards", HandleAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPut("/api/attendance/cards/{enrollmentId:guid}/cycle-start", SetCycleStartAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost("/api/attendance/cards/{enrollmentId:guid}/entries", AddEntryAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPut("/api/attendance/cards/entries/{entryId:guid}", ChangeEntryAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapDelete("/api/attendance/cards/entries/{entryId:guid}", RemoveEntryAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    public record EntryRequest(DateOnly Date);

    // Karta elle yazılan geçmiş ders (AD10): yönetici kâğıttaki son dersleri tarih olarak girer,
    // sonrasını sistemdeki yoklama doldurur. Ayrı bir "ilk giriş" seçimi yok (AD11): karta yazılan en
    // erken gün sayacın başlangıcıdır. O güne sistemde yoklamalı ders varsa satır açılmaz, yalnızca
    // sayaç o derse çekilir (ders zaten kartta görünür). Her değişiklikten sonra kart baştan sayılır
    // ve dönem aidatları eşitlenir.
    private static async Task<IResult> AddEntryAsync(
        Guid enrollmentId, EntryRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(e => e.Id == enrollmentId)
            ?? throw new NotFoundException("Kurs kaydı bulunamadı.");
        var movesStart = enrollment.CycleStartsOn is not { } own || request.Date < own;
        var onLessonDay = await ValidateEntryDateAsync(db, clock, enrollment, request.Date, exceptEntryId: null, movesStart);

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        Attendance.Domain.ManualAttendanceEntry? entry = null;
        if (!onLessonDay)
        {
            entry = Attendance.Domain.ManualAttendanceEntry.Create(enrollment.Id, request.Date, actorId, now);
            db.ManualAttendanceEntries.Add(entry);
            db.AuditLogs.Add(AuditLog.Record(
                actorId, "attendance.manual_entry_added", nameof(Attendance.Domain.ManualAttendanceEntry), entry.Id, now,
                afterJson: JsonSerializer.Serialize(new { enrollmentId = enrollment.Id, date = request.Date })));
        }
        if (movesStart) MoveCycleStart(db, enrollment, request.Date, actorId, now);
        await SaveEntryAsync(db);

        await CycleReceivableAccruer.SyncAsync(db, clock, enrollment, actorId);
        return entry is null
            ? Results.Ok(new { Id = (Guid?)null, Date = request.Date })
            : Results.Created($"/api/attendance/cards/entries/{entry.Id}", new { Id = (Guid?)entry.Id, entry.Date });
    }

    private static async Task<IResult> ChangeEntryAsync(
        Guid entryId, EntryRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var entry = await db.ManualAttendanceEntries.SingleOrDefaultAsync(e => e.Id == entryId)
            ?? throw new NotFoundException("Elle yazılmış ders bulunamadı.");
        var enrollment = await db.Enrollments.SingleAsync(e => e.Id == entry.EnrollmentId);
        if (entry.Date == request.Date) return Results.NoContent();
        var movesStart = enrollment.CycleStartsOn is not { } own || request.Date < own;
        // Elle satır sistem dersinin gününe taşınamaz (o gün zaten kartta); doğrulama bunu reddeder.
        await ValidateEntryDateAsync(db, clock, enrollment, request.Date, exceptEntryId: entry.Id, movesStart: false);

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var before = entry.Date;
        entry.ChangeDate(request.Date, now);
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "attendance.manual_entry_changed", nameof(Attendance.Domain.ManualAttendanceEntry), entry.Id, now,
            beforeJson: JsonSerializer.Serialize(new { date = before }),
            afterJson: JsonSerializer.Serialize(new { date = request.Date })));
        if (movesStart) MoveCycleStart(db, enrollment, request.Date, actorId, now);
        await SaveEntryAsync(db);
        if (!movesStart) await ReanchorCycleStartAsync(db, clock, enrollment, before, actorId);

        await CycleReceivableAccruer.SyncAsync(db, clock, enrollment, actorId);
        return Results.NoContent();
    }

    // Yanlış yazılmış satırı kaldırır; ne kaldırıldığı audit_log'da kalır (yoklama verisi, mali kayıt değil).
    private static async Task<IResult> RemoveEntryAsync(Guid entryId, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var entry = await db.ManualAttendanceEntries.SingleOrDefaultAsync(e => e.Id == entryId)
            ?? throw new NotFoundException("Elle yazılmış ders bulunamadı.");
        var enrollment = await db.Enrollments.SingleAsync(e => e.Id == entry.EnrollmentId);

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        db.ManualAttendanceEntries.Remove(entry);
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "attendance.manual_entry_removed", nameof(Attendance.Domain.ManualAttendanceEntry), entry.Id, now,
            beforeJson: JsonSerializer.Serialize(new { enrollmentId = enrollment.Id, date = entry.Date })));
        await db.SaveChangesAsync();
        await ReanchorCycleStartAsync(db, clock, enrollment, entry.Date, actorId);

        await CycleReceivableAccruer.SyncAsync(db, clock, enrollment, actorId);
        return Results.NoContent();
    }

    // Döndürür: o güne sistemde yoklamalı ders var mı (varsa satır açılmaz, yalnızca sayaç çekilir).
    private static async Task<bool> ValidateEntryDateAsync(
        AbderaDbContext db, IClock clock, Enrollment enrollment, DateOnly date, Guid? exceptEntryId, bool movesStart)
    {
        static ValidationFailedException Invalid(string message) =>
            new(new Dictionary<string, string[]> { ["date"] = [message] });

        if (!BillingCycle.AppliesTo(enrollment.CourseKind))
            throw Invalid("Yoklama kartı yalnızca birebir derslerde tutulur.");
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var earliest = EntriesFrom(today);
        if (date < earliest)
            throw Invalid($"Geriye dönük en fazla {earliest:dd.MM.yyyy} tarihine kadar ders yazılabilir.");
        if (date > today)
            throw Invalid("İleri bir tarihe ders yazılamaz; gelecek dersler yoklamadan gelir.");
        if (await db.ManualAttendanceEntries.AnyAsync(e => e.EnrollmentId == enrollment.Id && e.Date == date && e.Id != exceptEntryId))
            throw new ConflictException($"{date:dd.MM.yyyy} tarihine zaten bir ders yazılı.");

        var lessons = await CycleReceivableAccruer.LoadCardLessonsAsync(db, clock, enrollment, date);
        var onLessonDay = lessons.Any(lesson => lesson.LessonId is not null && lesson.Date == date);
        if (onLessonDay && !movesStart)
            throw new ConflictException($"{date:dd.MM.yyyy} tarihinde sistemde yoklaması girilmiş bir ders var; kartta o görünür.");
        return onLessonDay;
    }

    private static void MoveCycleStart(AbderaDbContext db, Enrollment enrollment, DateOnly to, Guid? actorId, DateTimeOffset now)
    {
        var before = enrollment.CycleStartsOn;
        if (before == to) return;
        enrollment.SetCycleStart(to, now);
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "enrollment.cycle_start_set", nameof(Enrollment), enrollment.Id, now,
            beforeJson: JsonSerializer.Serialize(new { cycleStartsOn = before }),
            afterJson: JsonSerializer.Serialize(new { cycleStartsOn = to, source = "first_card_row" })));
    }

    // Sayacı başlatan satır kaldırıldı ya da ileri alındı: sayaç kartta kalan ilk derse kayar. Kartta
    // ders kalmadıysa gün yerinde durur (yeni yazılan ilk satır onu yine belirler).
    private static async Task ReanchorCycleStartAsync(AbderaDbContext db, IClock clock, Enrollment enrollment, DateOnly removed, Guid? actorId)
    {
        if (enrollment.CycleStartsOn != removed) return;
        var first = (await CycleReceivableAccruer.LoadCardLessonsAsync(db, clock, enrollment, removed))
            .FirstOrDefault(lesson => CycleReceivableAccruer.Fills(lesson.Status));
        if (first is null || first.Date == removed) return;
        MoveCycleStart(db, enrollment, first.Date, actorId, clock.UtcNow);
        await db.SaveChangesAsync();
    }

    private static async Task SaveEntryAsync(AbderaDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Bu tarihe az önce bir ders yazıldı. Kartı yenileyip tekrar deneyin.");
        }
    }

    public record CycleStartRequest(DateOnly? CycleStartsOn);

    // Öğrencinin dönem sayacının başladığı gün (ilk giriş, ay ortası olabilir - AD9). Değişince kart
    // baştan sayılır ve dönem aidatları yeni başlangıca göre eşitlenir (CycleReceivableAccruer).
    private static async Task<IResult> SetCycleStartAsync(
        Guid enrollmentId, CycleStartRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(e => e.Id == enrollmentId)
            ?? throw new NotFoundException("Kurs kaydı bulunamadı.");
        if (!BillingCycle.AppliesTo(enrollment.CourseKind))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["cycleStartsOn"] = ["Dönem başlangıcı yalnızca birebir derslerde girilir; grup dersi aylık aidatla devam eder."],
            });

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var before = enrollment.CycleStartsOn;
        if (before == request.CycleStartsOn) return Results.NoContent();

        enrollment.SetCycleStart(request.CycleStartsOn, now);
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "enrollment.cycle_start_set", nameof(Enrollment), enrollment.Id, now,
            beforeJson: JsonSerializer.Serialize(new { cycleStartsOn = before }),
            afterJson: JsonSerializer.Serialize(new { cycleStartsOn = request.CycleStartsOn })));
        await db.SaveChangesAsync();

        await CycleReceivableAccruer.SyncAsync(db, clock, enrollment, actorId);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleAsync(AbderaDbContext db, IClock clock)
    {
        var settings = await BillingSettings.GetCurrentAsync(db);

        var enrollments = await db.Enrollments.AsNoTracking()
            .Where(e => e.Status == EnrollmentStatus.Active && e.CourseKind == CourseKind.Individual)
            .ToListAsync();
        var enrollmentIds = enrollments.Select(e => e.Id).ToList();
        var studentIds = enrollments.Select(e => e.StudentId).Distinct().ToList();
        var teacherIds = enrollments.Select(e => e.TeacherId).Distinct().ToList();
        var instrumentIds = enrollments.Select(e => e.InstrumentId).Distinct().ToList();

        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        var instruments = await db.Instruments.AsNoTracking().Where(i => instrumentIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

        var cycleReceivables = (await db.Receivables.AsNoTracking()
                .Where(r => enrollmentIds.Contains(r.EnrollmentId) && r.Period.StartsWith("D") && r.Status != ReceivableStatus.Cancelled)
                .ToListAsync())
            .Where(r => BillingCycle.IsKey(r.Period))
            .ToList();
        // Sayacın başladığı ayın aylık aidatı 1. dönemi karşılar (AD9) - kartta 1. dönemin aidatı olarak görünür.
        var coveringMonthly = new Dictionary<Guid, Receivable>();
        foreach (var enrollment in enrollments)
        {
            if (CycleReceivableAccruer.CountingFrom(enrollment, settings) is { } start
                && await CycleReceivableAccruer.FindCoveringMonthlyAsync(db, clock, enrollment.Id, start) is { } monthly)
                coveringMonthly[enrollment.Id] = monthly;
        }
        var paid = await Receivables.ComputeTotalsPaidAsync(
            cycleReceivables.Select(r => r.Id).Concat(coveringMonthly.Values.Select(r => r.Id)), db);
        var receivablesByEnrollment = cycleReceivables.ToLookup(r => r.EnrollmentId);

        var cards = new List<Card>();
        foreach (var enrollment in enrollments)
        {
            if (!students.TryGetValue(enrollment.StudentId, out var student)) continue;
            var countingFrom = CycleReceivableAccruer.CountingFrom(enrollment, settings);
            // Tek sorgu: kayıt başından itibaren; sayaç öncesi kısım karttan ayrılıp bilgi olarak döner.
            var loadFrom = countingFrom is { } c && c < enrollment.StartedAt ? c : enrollment.StartedAt;
            var all = await CycleReceivableAccruer.LoadCardLessonsAsync(db, clock, enrollment, loadFrom);
            var lessons = countingFrom is { } from ? all.Where(lesson => lesson.Date >= from).ToList() : [];
            var earlier = countingFrom is { } until ? all.Where(lesson => lesson.Date < until).ToList() : all;
            var receivables = receivablesByEnrollment[enrollment.Id].ToDictionary(r => BillingCycle.Number(r.Period));

            cards.Add(Build(
                enrollment, $"{student.FirstName} {student.LastName}",
                teachers.TryGetValue(enrollment.TeacherId, out var teacher) ? $"{teacher.FirstName} {teacher.LastName}" : "Öğretmen",
                instruments.TryGetValue(enrollment.InstrumentId, out var instrument) ? instrument.Name : "Ders",
                countingFrom, lessons, receivables, paid, earlier, coveringMonthly.GetValueOrDefault(enrollment.Id)));
        }

        // Türkçe alfabetik sıra istemcide (localeCompare "tr") - BillingBoard ile aynı gerekçe.
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        return Results.Ok(new CardsResponse(settings.CycleBillingStartsOn, cards, EntriesFrom(today)));
    }

    // Saf hesap: dersleri dönemlere dağıtır ve kartın durumunu çıkarır (birim testlenir).
    public static Card Build(
        Enrollment enrollment, string studentName, string teacherName, string instrumentName, DateOnly? countingFrom,
        IReadOnlyList<CycleReceivableAccruer.CountedLesson> lessons,
        IReadOnlyDictionary<int, Receivable> receivables,
        IReadOnlyDictionary<Guid, decimal> paid,
        IReadOnlyList<CycleReceivableAccruer.CountedLesson>? earlierLessons = null,
        Receivable? coveringMonthly = null)
    {
        // Sayacın başladığı ayın aylık aidatı 1. dönemin aidatıdır (AD9).
        if (coveringMonthly is not null && !receivables.ContainsKey(1))
            receivables = new Dictionary<int, Receivable>(receivables) { [1] = coveringMonthly };

        var filled = new List<List<CardLesson>>();
        var misses = new Dictionary<int, List<CardLesson>>();
        var filledCount = 0;
        foreach (var lesson in lessons)
        {
            var item = new CardLesson(lesson.LessonId, lesson.StartAt, lesson.Date, lesson.Status, lesson.EntryId);
            if (CycleReceivableAccruer.Fills(lesson.Status))
            {
                var cycle = BillingCycle.CycleOf(filledCount);
                if (filled.Count < cycle) filled.Add([]);
                filled[cycle - 1].Add(item);
                filledCount++;
            }
            else
            {
                // Gelmedi, o an süren dönemin altına yazılır (4/4 dolmuşsa da o dönemde kalır).
                var cycle = filledCount == 0 ? 1 : BillingCycle.CycleOf(filledCount - 1);
                if (!misses.TryGetValue(cycle, out var list)) misses[cycle] = list = [];
                list.Add(item);
            }
        }

        var current = BillingCycle.RequiredCycles(filledCount);
        var filledInCurrent = current == 0 ? 0 : filledCount - BillingCycle.OpeningLessonIndex(current);
        var lastNumber = new[] { current, misses.Keys.DefaultIfEmpty(0).Max(), receivables.Keys.DefaultIfEmpty(0).Max() }.Max();

        CycleReceivable? ToCycleReceivable(int number) =>
            receivables.TryGetValue(number, out var r)
                ? new CycleReceivable(r.Id, r.Status, r.Amount, paid.GetValueOrDefault(r.Id), r.Currency, r.DueDate,
                    r == coveringMonthly ? countingFrom : r.AccruedOn)
                : null;

        var cycles = Enumerable.Range(1, lastNumber)
            .Select(number => new CardCycle(
                number,
                number <= filled.Count ? filled[number - 1] : [],
                misses.GetValueOrDefault(number) ?? [],
                ToCycleReceivable(number)))
            .ToList();

        // Ödenmemiş: başlamış (tahakkuk etmiş) dönemlerin kalan borcu.
        var unpaid = receivables.Values
            .Where(r => (r.AccruedOn is not null || r == coveringMonthly) && r.Status is not (ReceivableStatus.Paid or ReceivableStatus.Cancelled))
            .Sum(r => r.Amount - paid.GetValueOrDefault(r.Id));

        var state = unpaid > 0 ? CardState.Unpaid
            : current == 0 ? CardState.NotStarted
            : filledInCurrent == BillingCycle.LessonsPerCycle
                ? receivables.ContainsKey(current + 1) ? CardState.NextPrepaid : CardState.AwaitingPayment
            : filledInCurrent == BillingCycle.LessonsPerCycle - 1 ? CardState.LastLesson
            : CardState.InProgress;

        return new Card(
            enrollment.Id, enrollment.StudentId, studentName, enrollment.TeacherId, teacherName, instrumentName,
            enrollment.StartedAt, countingFrom, current, filledInCurrent, state, unpaid,
            receivables.Values.Select(r => r.Currency).FirstOrDefault() ?? "TRY",
            lessons.Count > 0 ? lessons[^1].Date : earlierLessons is { Count: > 0 } ? earlierLessons[^1].Date : null,
            cycles,
            (earlierLessons ?? []).Select(lesson => new CardLesson(lesson.LessonId, lesson.StartAt, lesson.Date, lesson.Status)).ToList(),
            enrollment.CycleStartsOn is not null);
    }
}
