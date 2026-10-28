using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Infrastructure;

// Kurs kaydının aidatı belirleyen alanı (ders türü, özel indirim) değişince o kaydın ÖDEME
// GÖRMEMİŞ açık aidatlarını yeni hesaba çeker. Sebep (ürün sahibi): Resim kayıtları Grup'a
// alındıktan sonra Aidatlar çizelgesinde Ekim hâlâ Birebir tutarıyla (6.000 TL) görünüyordu -
// satır ders türü Birebir iken açılmış ve fiyat snapshot'ı donmuştu.
//
// Snapshot kuralını bozmaz: para girmiş (Paid/Partial ya da ödemesi olan Overdue), iptal
// edilmiş veya peşin plana bağlı satıra dokunmaz. Değişen her satır audit_log'a yazılır.
// Çağıran SaveChanges'i kendisi yapar; enrollment'ın yeni alanları bellekte uygulanmış olmalı.
public static class EnrollmentReceivableRepricer
{
    public static async Task<int> RepriceUnpaidAsync(
        Enrollment enrollment, AbderaDbContext db, Guid? actorId, DateTimeOffset now)
    {
        var candidates = await db.Receivables
            .Where(r => r.EnrollmentId == enrollment.Id && r.PrepayPlanId == null)
            .Where(r => r.Status == ReceivableStatus.Unpaid || r.Status == ReceivableStatus.Overdue)
            .ToListAsync();
        if (candidates.Count == 0) return 0;

        var paid = await Receivables.ComputeTotalsPaidAsync(candidates.Select(r => r.Id), db);
        var pricer = await TuitionPricer.LoadAsync(db);
        var changed = 0;

        foreach (var receivable in candidates.Where(r => paid.GetValueOrDefault(r.Id) == 0m))
        {
            // Dönem aidatı tahakkuk gününün (peşin, başlamamışsa açıldığı günün) tarifesiyle fiyatlanır.
            var priced = BillingCycle.IsKey(receivable.Period)
                ? pricer.PriceOn(enrollment, receivable.AccruedOn ?? DateOnly.FromDateTime(receivable.CreatedAt.UtcDateTime))
                : pricer.Price(enrollment, receivable.Period);
            if (priced is null) continue;

            var breakdown = priced.Value.Breakdown;
            if (breakdown.BaseAmount == receivable.BaseAmount && breakdown.DiscountPercent == receivable.DiscountPercent
                && breakdown.DiscountReason == receivable.DiscountReason && breakdown.NetAmount == receivable.Amount)
                continue;

            var before = JsonSerializer.Serialize(new
            {
                period = receivable.Period,
                baseAmount = receivable.BaseAmount,
                discountPercent = receivable.DiscountPercent,
                discountReason = receivable.DiscountReason,
                amount = receivable.Amount,
            });
            receivable.Reprice(breakdown, null, now);
            db.AuditLogs.Add(AuditLog.Record(
                actorId, "receivable.repriced_after_enrollment_change", nameof(Receivable), receivable.Id, now,
                beforeJson: before,
                afterJson: JsonSerializer.Serialize(new
                {
                    period = receivable.Period,
                    courseKind = enrollment.CourseKind.ToString(),
                    baseAmount = receivable.BaseAmount,
                    discountPercent = receivable.DiscountPercent,
                    discountReason = receivable.DiscountReason,
                    amount = receivable.Amount,
                })));
            changed++;
        }

        return changed;
    }
}
