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

    public record CardLesson(Guid LessonId, DateTimeOffset StartAt, DateOnly Date, AttendanceStatus Status);

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

    public record CardsResponse(DateOnly? CycleBillingStartsOn, List<Card> Cards);

    public static void MapAttendanceCards(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/attendance/cards", HandleAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPut("/api/attendance/cards/{enrollmentId:guid}/cycle-start", SetCycleStartAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
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
                && await CycleReceivableAccruer.FindCoveringMonthlyAsync(db, enrollment.Id, start) is { } monthly)
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
        return Results.Ok(new CardsResponse(settings.CycleBillingStartsOn, cards));
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
            var item = new CardLesson(lesson.LessonId, lesson.StartAt, lesson.Date, lesson.Status);
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
