using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Domain;

// Grup dersi (Resim) öğretmen ücreti (docs/10-decisions.md AC). Kullanıcı kuralı: "aylık
// gelirin %40'ı olarak hesaplanıyor" - gelir TAHAKKUKTUR (dönemin iptal edilmemiş grup
// aidatları, ödenmiş olsun olmasın), ödeme ayda bir yapılır. Grup dersi satırları bu yüzden
// haftalık ders başı ödemeden (TeacherPayouts) ve kârlılığın ders başı maliyetinden çıkarılır;
// yoksa öğretmen aynı dersi iki kez almış olurdu. Hesabın tek yeri burası.
public static class GroupLessonPay
{
    // Varsayılan pay; okulun geçerli değeri school_policy'dedir (SchoolPolicy.GroupLessonSharePercent).
    public const decimal SharePercent = 40m;

    public static decimal ComputeAmount(decimal revenueBase, decimal sharePercent = SharePercent)
    {
        if (revenueBase < 0) throw new ArgumentOutOfRangeException(nameof(revenueBase), "Gelir negatif olamaz.");
        return decimal.Round(revenueBase * sharePercent / 100m, 2, MidpointRounding.AwayFromZero);
    }

    // Grup dersi satırı: ders, öğrencinin o öğretmen + kurs için Grup türündeki kaydından doğmuş.
    // Ders satırı kayda değil (öğrenci, öğretmen, kurs) üçlüsüne bağlı olduğu için eşleşme bu
    // üçlü üzerinden yapılır; telafi dersleri (seriye bağlı değil) de böylece doğru ayrılır.
    public static IQueryable<Lesson> WithoutGroupLessons(this IQueryable<Lesson> lessons, AbderaDbContext db) =>
        lessons.Where(lesson => !db.Enrollments.Any(enrollment =>
            enrollment.CourseKind == CourseKind.Group
            && enrollment.StudentId == lesson.StudentId
            && enrollment.TeacherId == lesson.TeacherId
            && enrollment.InstrumentId == lesson.InstrumentId));

    public static IQueryable<Lesson> OnlyGroupLessons(this IQueryable<Lesson> lessons, AbderaDbContext db) =>
        lessons.Where(lesson => db.Enrollments.Any(enrollment =>
            enrollment.CourseKind == CourseKind.Group
            && enrollment.StudentId == lesson.StudentId
            && enrollment.TeacherId == lesson.TeacherId
            && enrollment.InstrumentId == lesson.InstrumentId));

    public record RevenueRow(Guid TeacherId, string Period, decimal RevenueBase, int ReceivableCount, string Currency);

    // Dönem(ler)in grup geliri, öğretmen kırılımında: Grup türündeki kayıtların iptal
    // edilmemiş aidatlarının tutarı (indirim sonrası, yani gerçekten tahakkuk eden).
    public static async Task<List<RevenueRow>> RevenueByTeacherAsync(
        AbderaDbContext db, IReadOnlyCollection<string> periods, Guid? teacherId = null, CancellationToken cancellationToken = default)
    {
        var query = db.Receivables.AsNoTracking()
            .Where(receivable => periods.Contains(receivable.Period) && receivable.Status != ReceivableStatus.Cancelled)
            .Join(db.Enrollments.Where(enrollment => enrollment.CourseKind == CourseKind.Group),
                receivable => receivable.EnrollmentId, enrollment => enrollment.Id,
                (receivable, enrollment) => new { receivable.Period, receivable.Amount, receivable.Currency, enrollment.TeacherId });
        if (teacherId is { } id) query = query.Where(row => row.TeacherId == id);

        var rows = await query.ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => new { row.TeacherId, row.Period })
            .Select(group => new RevenueRow(
                group.Key.TeacherId, group.Key.Period, group.Sum(row => row.Amount), group.Count(),
                group.Select(row => row.Currency).First()))
            .ToList();
    }
}
