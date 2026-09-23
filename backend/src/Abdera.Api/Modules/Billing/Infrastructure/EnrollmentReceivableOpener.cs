using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Abdera.Api.Modules.Billing.Infrastructure;

// İş kuralı (ürün sahibi): "Bir öğrenci kayıtlıysa her ay ödeme yapabiliyor olmalı."
// Aidatları yalnızca MonthlyReceivableGenerator açıyordu (açılışta + 24 saatte bir); ay
// içinde açılan yeni bir kurs kaydı o ayın aidat satırını bir sonraki tike kadar (en fazla
// 24 saat) göremiyor, Aylık aidatlar ekranında tahsilat alınamıyordu. People modülü kurs
// kaydını açtıktan hemen sonra bu açık servisi çağırır (CLAUDE.md: modüller arası erişim
// navigation join değil, açık bir servis üzerinden).
//
// Fiyatlama MonthlyDueRun ile birebir aynı: TuitionPricer + Receivable.Create + audit_log.
// Tarife yoksa hiçbir şey yazmaz ve hata FIRLATMAZ - kurs kaydının açılması aidata bağlı
// değil; eksik tarife Aylık aidatlar ekranında "Bu ayın aidatını aç" denemesiyle açık bir
// mesaj olarak görünür (POST /api/receivables).
public static class EnrollmentReceivableOpener
{
    public enum Outcome
    {
        Opened,
        AlreadyExists,
        NotActive,
        StartsInLaterPeriod,
        NoTariff,
    }

    // Kurs kaydı bu çağrıdan ÖNCE kaydedilmiş olmalı: TuitionPricer çoklu kurs indirimini
    // veritabanındaki aktif kayıt sayısından çıkarır, kaydedilmemiş yeni kayıt sayılmazdı.
    public static async Task<Outcome> OpenCurrentPeriodAsync(
        AbderaDbContext db, IClock clock, Guid enrollmentId, Guid? actorId)
    {
        var enrollment = await db.Enrollments.AsNoTracking().SingleOrDefaultAsync(e => e.Id == enrollmentId);
        if (enrollment is null || enrollment.Status != EnrollmentStatus.Active) return Outcome.NotActive;

        // Okulun yerel takvimindeki dönem - MonthlyReceivableGenerator ile aynı hesap.
        var now = clock.UtcNow;
        var today = clock.ToSchoolLocal(now);
        var period = BillingPeriod.Format(today.Year, today.Month);

        // Başlangıcı ileri bir ayda olan kayıt bu ayın borcunu doğurmaz; o ayın aidatını
        // zamanı gelince aylık üretim açar.
        if (enrollment.StartedAt > BillingPeriod.FirstDay(period).AddMonths(1).AddDays(-1))
            return Outcome.StartsInLaterPeriod;

        if (await db.Receivables.AnyAsync(r => r.EnrollmentId == enrollmentId && r.Period == period))
            return Outcome.AlreadyExists;

        var pricer = await TuitionPricer.LoadAsync(db);
        var priced = pricer.Price(enrollment, period);
        if (priced is null) return Outcome.NoTariff;

        var receivable = Receivable.Create(
            enrollment.Id, priced.Value.Rate.Id, period, priced.Value.Breakdown,
            priced.Value.Rate.Currency, pricer.DueDateFor(period), now);
        var audit = AuditLog.Record(
            actorId, "receivable.enrollment_opened", nameof(Receivable), receivable.Id, now,
            afterJson: JsonSerializer.Serialize(new
            {
                period,
                baseAmount = receivable.BaseAmount,
                discountPercent = receivable.DiscountPercent,
                discountReason = receivable.DiscountReason,
                amount = receivable.Amount,
                currency = receivable.Currency,
                enrollmentId = receivable.EnrollmentId,
            }));

        db.Receivables.Add(receivable);
        db.AuditLogs.Add(audit);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı anda çalışan MonthlyReceivableGenerator aynı ayı açmış olabilir - UNIQUE
            // (enrollment_id, period) ikincisini reddeder. Sonuç zaten istenen durum; context'i
            // temizleyip sessizce devam et (kurs kaydının yanıtı bu yüzden 500 olmamalı).
            db.Entry(receivable).State = EntityState.Detached;
            db.Entry(audit).State = EntityState.Detached;
            return Outcome.AlreadyExists;
        }

        return Outcome.Opened;
    }
}
