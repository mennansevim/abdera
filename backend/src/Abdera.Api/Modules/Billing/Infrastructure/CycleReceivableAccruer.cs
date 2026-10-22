using System.Text.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Abdera.Api.Modules.Billing.Infrastructure;

// Dönem aidatının tek tahakkuk yeri (docs/10-decisions.md AD, BillingCycle). Attendance modülü
// her yoklama yazışından sonra bu açık servisi çağırır; kart (Geldi + Mazeretli) baştan sayılır
// ve olması gereken dönem aidatları ile mevcutlar eşitlenir. Yeniden saymak, "bu yoklama kaçıncı
// satır" diye artımlı tutmaktan sağlam: geçmiş bir dersin yoklaması düzeltildiğinde de doğru sonuç
// çıkar ve günde kaç kez çağrılırsa çağrılsın aynı yere varır.
//
// Kural: n. dönemin ilk dersi işlendiğinde n. dönem aidatı açılır. Peşin ödenmiş dönem zaten
// vardır, yalnızca o derse bağlanır (tahakkuk eder). Yoklama düzeltmesi bir dönemi geri alırsa
// ödenmemiş aidat iptal edilir, ödenmiş olan "peşin, henüz başlamamış" hâline döner.
public static class CycleReceivableAccruer
{
    public record CountedLesson(Guid LessonId, DateTimeOffset StartAt, DateOnly Date, AttendanceStatus Status);

    // Kartta sayılan (Geldi + Mazeretli) ve sayılmayan (Gelmedi) dersler, tarih sırasıyla.
    public static async Task<List<CountedLesson>> LoadCardLessonsAsync(
        AbderaDbContext db, IClock clock, Enrollment enrollment, DateOnly countingFrom)
    {
        var from = LessonGenerator.ToUtcInstant(countingFrom, TimeOnly.MinValue, clock.SchoolTimeZone);
        DateTimeOffset? until = enrollment.EndedAt is { } ended
            ? LessonGenerator.ToUtcInstant(ended.AddDays(1), TimeOnly.MinValue, clock.SchoolTimeZone)
            : null;

        // Modüller arası okuma açık id sorgularıyla (CLAUDE.md). Ders ile kurs kaydı arasında FK yok:
        // birebir kayıt öğrenci + enstrümanla eşleşir (öğretmen değişse de kart aynı kalır).
        var rows = await db.Lessons.AsNoTracking()
            .Where(lesson => lesson.StudentId == enrollment.StudentId && lesson.InstrumentId == enrollment.InstrumentId
                && lesson.StartAt >= from && (until == null || lesson.StartAt < until))
            .Join(db.LessonAttendances.AsNoTracking(), lesson => lesson.Id, attendance => attendance.LessonId,
                (lesson, attendance) => new { lesson.Id, lesson.StartAt, attendance.Status })
            .OrderBy(row => row.StartAt).ThenBy(row => row.Id)
            .ToListAsync();

        return rows
            .Select(row => new CountedLesson(row.Id, row.StartAt, DateOnly.FromDateTime(clock.ToSchoolLocal(row.StartAt).Date), row.Status))
            .ToList();
    }

    public static bool Fills(AttendanceStatus status) => status is AttendanceStatus.Present or AttendanceStatus.Excused;

    // Kartın saymaya başladığı gün (BillingCycle.StartFor). Null: bu kayıt dönemle faturalanmıyor.
    public static DateOnly? CountingFrom(Enrollment enrollment, BillingSettings settings) =>
        BillingCycle.StartFor(enrollment.CourseKind, enrollment.CycleStartsOn, enrollment.StartedAt, settings.CycleBillingStartsOn);

    // Sayacın başladığı ayın aylık aidatı (geçiş öncesi açılmış): varsa 1. dönemin ücreti sayılır.
    public static async Task<Receivable?> FindCoveringMonthlyAsync(AbderaDbContext db, Guid enrollmentId, DateOnly countingFrom)
    {
        var month = BillingPeriod.Format(countingFrom.Year, countingFrom.Month);
        return await db.Receivables.AsNoTracking()
            .SingleOrDefaultAsync(r => r.EnrollmentId == enrollmentId && r.Period == month && r.Status != ReceivableStatus.Cancelled);
    }

    // Bu dersin yoklaması bir dönem kartına mı sayılıyor - Mazeretli ders kartta satır doldurduğu
    // için ayrıca telafi hakkı doğurmaz (ExcusedMakeupCredit'i çağıran MarkAttendance buna bakar).
    public static async Task<bool> CountsOnCardAsync(AbderaDbContext db, IClock clock, Lesson lesson)
    {
        var enrollment = await FindEnrollmentAsync(db, lesson.StudentId, lesson.InstrumentId);
        if (enrollment is null) return false;
        var from = CountingFrom(enrollment, await BillingSettings.GetCurrentAsync(db));
        return from is { } start && DateOnly.FromDateTime(clock.ToSchoolLocal(lesson.StartAt).Date) >= start;
    }

    // Yoklaması yazılan dersin kurs kaydını bulur ve kartını eşitler. Yoklama zaten kaydedilmiş olmalı.
    public static async Task SyncForLessonAsync(AbderaDbContext db, IClock clock, Lesson lesson, Guid? actorId)
    {
        var enrollment = await FindEnrollmentAsync(db, lesson.StudentId, lesson.InstrumentId);
        if (enrollment is null) return;

        try
        {
            await SyncAsync(db, clock, enrollment, actorId);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı öğrencinin iki yoklaması aynı anda yazıldı ve ikisi de aynı dönemi açmaya çalıştı -
            // UNIQUE (enrollment_id, period) ikincisini reddetti. Yeniden saymak doğru sonucu verir.
            foreach (var entry in db.ChangeTracker.Entries<Receivable>().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            foreach (var entry in db.ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            await SyncAsync(db, clock, enrollment, actorId);
        }
    }

    private static Task<Enrollment?> FindEnrollmentAsync(AbderaDbContext db, Guid studentId, Guid instrumentId) =>
        db.Enrollments
            .Where(e => e.StudentId == studentId && e.InstrumentId == instrumentId && e.CourseKind == CourseKind.Individual)
            .OrderByDescending(e => e.Status == EnrollmentStatus.Active)
            .ThenByDescending(e => e.StartedAt)
            .FirstOrDefaultAsync();

    public static async Task SyncAsync(AbderaDbContext db, IClock clock, Enrollment enrollment, Guid? actorId)
    {
        var settings = await BillingSettings.GetCurrentAsync(db);
        if (CountingFrom(enrollment, settings) is not { } countingFrom) return;

        var counted = (await LoadCardLessonsAsync(db, clock, enrollment, countingFrom))
            .Where(lesson => Fills(lesson.Status))
            .ToList();
        var required = BillingCycle.RequiredCycles(counted.Count);
        // Sayacın başladığı ayın aylık aidatı 1. dönemi karşılar (AD9): D001 açılmaz.
        var firstCovered = await FindCoveringMonthlyAsync(db, enrollment.Id, countingFrom) is not null;

        var cycles = (await db.Receivables
                .Where(r => r.EnrollmentId == enrollment.Id && r.Period.StartsWith("D"))
                .ToListAsync())
            .Where(r => BillingCycle.IsKey(r.Period))
            .ToDictionary(r => BillingCycle.Number(r.Period));

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        TuitionPricer? pricer = null;

        for (var number = firstCovered ? 2 : 1; number <= required; number++)
        {
            var opening = counted[BillingCycle.OpeningLessonIndex(number)];
            var dueDate = opening.Date.AddDays(BillingCycle.DueDays);
            cycles.TryGetValue(number, out var receivable);

            if (receivable is not null && receivable.Status != ReceivableStatus.Cancelled)
            {
                if (receivable.AccruedLessonId == opening.LessonId) continue;
                var wasPrepaid = receivable.AccruedLessonId is null;
                receivable.MarkAccrued(opening.LessonId, opening.Date, dueDate, today, now);
                db.AuditLogs.Add(AuditLog.Record(
                    actorId, wasPrepaid ? "receivable.cycle_accrued_prepaid" : "receivable.cycle_reaccrued",
                    nameof(Receivable), receivable.Id, now,
                    afterJson: JsonSerializer.Serialize(new
                    {
                        period = receivable.Period,
                        accruedOn = opening.Date,
                        lessonId = opening.LessonId,
                        dueDate = receivable.DueDate,
                    })));
                continue;
            }

            pricer ??= await TuitionPricer.LoadAsync(db);
            // Fiyat snapshot'ı dönemin başladığı günün tarifesiyle donar.
            var priced = pricer.PriceOn(enrollment, opening.Date);
            if (priced is null)
            {
                // Eksik tarife okulun kurulum eksiği; yoklama yazımını düşürmez, dönem tarife
                // tanımlanınca bir sonraki yoklamada (ya da kart ekranındaki yeniden saymada) açılır.
                continue;
            }

            var (rate, breakdown) = priced.Value;
            if (receivable is not null)
            {
                receivable.ReopenCycle(rate.Id, breakdown, rate.Currency, opening.LessonId, opening.Date, dueDate, now);
                db.AuditLogs.Add(AuditLog.Record(
                    actorId, "receivable.cycle_reopened", nameof(Receivable), receivable.Id, now,
                    beforeJson: JsonSerializer.Serialize(new { status = ReceivableStatus.Cancelled.ToString() }),
                    afterJson: CycleJson(receivable, opening)));
                continue;
            }

            var created = Receivable.CreateCycle(
                enrollment.Id, rate.Id, number, breakdown, rate.Currency, dueDate, now, opening.Date, opening.LessonId);
            db.Receivables.Add(created);
            db.AuditLogs.Add(AuditLog.Record(
                actorId, "receivable.cycle_accrued", nameof(Receivable), created.Id, now,
                afterJson: CycleJson(created, opening)));
        }

        // Kartta artık var olmayan dönemler (yoklama Geldi'den Gelmedi'ye düzeltildi).
        var stale = cycles.Where(pair => (pair.Key > required || (firstCovered && pair.Key == 1)) && pair.Value.AccruedLessonId is not null
            && pair.Value.Status != ReceivableStatus.Cancelled).Select(pair => pair.Value).ToList();
        if (stale.Count > 0)
        {
            var paid = await Features.Receivables.ComputeTotalsPaidAsync(stale.Select(r => r.Id), db);
            foreach (var receivable in stale)
            {
                var before = JsonSerializer.Serialize(new
                {
                    period = receivable.Period,
                    status = receivable.Status.ToString(),
                    accruedOn = receivable.AccruedOn,
                    lessonId = receivable.AccruedLessonId,
                });
                var hasPayment = paid.GetValueOrDefault(receivable.Id) > 0m;
                if (hasPayment) receivable.Unaccrue(now);
                else receivable.Cancel(now);
                db.AuditLogs.Add(AuditLog.Record(
                    actorId, hasPayment ? "receivable.cycle_unaccrued" : "receivable.cycle_cancelled",
                    nameof(Receivable), receivable.Id, now, beforeJson: before,
                    afterJson: JsonSerializer.Serialize(new
                    {
                        status = receivable.Status.ToString(),
                        reason = "Yoklama düzeltmesi ya da dönem başlangıcı değişikliği dönemi geri aldı.",
                    })));
            }
        }

        await db.SaveChangesAsync();
    }

    private static string CycleJson(Receivable receivable, CountedLesson opening) => JsonSerializer.Serialize(new
    {
        period = receivable.Period,
        accruedOn = opening.Date,
        lessonId = opening.LessonId,
        baseAmount = receivable.BaseAmount,
        discountPercent = receivable.DiscountPercent,
        discountReason = receivable.DiscountReason,
        amount = receivable.Amount,
        currency = receivable.Currency,
        dueDate = receivable.DueDate,
        enrollmentId = receivable.EnrollmentId,
    });
}
