using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Abdera.Api.Modules.Billing.Infrastructure;

// Aidatın günlük bakımı: bu ayın aidatlarını açmak (MonthlyReceivableGenerator) ve vadesi
// geçenleri Overdue'ya çevirmek (OverdueReceivableSweeper). Mantık tek yerde durur; iki arka
// plan servisi de buradan çağırır, testler de doğrudan bunu çalıştırır.
public static class BillingDailyJob
{
    public record Result(string Period, int CreatedCount, int MissingTariffCount, int MarkedOverdueCount);

    public static async Task<Result> RunAsync(AbderaDbContext db, IClock clock, CancellationToken cancellationToken = default)
    {
        var opened = await OpenCurrentPeriodAsync(db, clock);
        var overdue = await MarkOverdueAsync(db, clock, cancellationToken);
        return new Result(opened.Period, opened.CreatedCount, opened.Missing.Count, overdue);
    }

    // Bu ayın (okulun yerel takvimi) eksik aidatlarını açar. İdempotent: MonthlyDueRun zaten var
    // olanları atlar. Aynı anda iki çağrı (cron + ekran) aynı satırı yazmaya çalışırsa UNIQUE
    // (enrollment_id, period) ikincisini reddeder; o durumda plan yeniden kurulur ve diğer
    // çağrının açtıkları atlanır.
    public static async Task<MonthlyDueRun.CreateResponse> OpenCurrentPeriodAsync(AbderaDbContext db, IClock clock)
    {
        var today = clock.ToSchoolLocal(clock.UtcNow);
        var period = BillingPeriod.Format(today.Year, today.Month);
        try
        {
            return await MonthlyDueRun.RunAsync(db, clock, period, actorId: null, throwIfEmpty: false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return await MonthlyDueRun.RunAsync(db, clock, period, actorId: null, throwIfEmpty: false);
        }
    }

    public static async Task<int> MarkOverdueAsync(AbderaDbContext db, IClock clock, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);

        var candidates = await db.Receivables
            .Where(r => (r.Status == ReceivableStatus.Unpaid || r.Status == ReceivableStatus.Partial) && r.DueDate < today)
            .ToListAsync(cancellationToken);

        foreach (var receivable in candidates)
        {
            receivable.MarkOverdueIfPastDue(today, now);
        }

        if (candidates.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return candidates.Count;
    }
}
