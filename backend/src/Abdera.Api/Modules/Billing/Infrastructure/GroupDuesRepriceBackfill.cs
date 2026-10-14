using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Infrastructure;

// Tek seferlik düzeltme (2026-10-15). EnrollmentReceivableRepricer yalnızca ders türü DEĞİŞTİĞİ
// anda çalışır; Resim kayıtları o düzeltme canlıya çıkmadan önce Grup'a alındığı için açılmış
// Ekim aidatları Birebir tutarında (6.000) kaldı ve ödeme penceresi 6.000 tahsil etmeye hazırdı.
// Her Grup kaydının ödeme görmemiş açık aidatı aynı repricer'dan geçer - para girmiş, iptal
// edilmiş ve peşin plana bağlı satırlar yine dokunulmadan kalır, her değişiklik audit_log'a yazılır.
//
// Açılışta bir kez çalışır: bitince audit_log'a işaret satırı düşer (değişen satır sayısıyla);
// işaret varsa bir daha çalışmaz, böylece ileride açılan aylar sessizce yeniden fiyatlanmaz.
public static class GroupDuesRepriceBackfill
{
    public const string MarkerAction = "maintenance.group_dues_repriced_backfill";

    public static async Task RunOnceAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            if (await db.AuditLogs.AnyAsync(log => log.Action == MarkerAction)) return;

            var now = clock.UtcNow;
            var changed = await RepriceGroupEnrollmentsAsync(db, now);
            db.AuditLogs.Add(AuditLog.Record(
                null, MarkerAction, nameof(Enrollment), Guid.Empty, now,
                afterJson: JsonSerializer.Serialize(new { repricedReceivables = changed })));
            await db.SaveChangesAsync();

            logger.LogInformation("Grup aidat düzeltmesi: {Count} açık aidat yeniden fiyatlandı.", changed);
        }
        catch (Exception exception)
        {
            // Düzeltme açılışı düşürmemeli; işaret yazılmadığı için bir sonraki açılışta yeniden dener.
            logger.LogError(exception, "Grup aidat düzeltmesi tamamlanamadı.");
        }
    }

    // Çağıran SaveChanges'i yapar.
    public static async Task<int> RepriceGroupEnrollmentsAsync(AbderaDbContext db, DateTimeOffset now)
    {
        var enrollments = await db.Enrollments.Where(e => e.CourseKind == CourseKind.Group).ToListAsync();
        var changed = 0;
        foreach (var enrollment in enrollments)
            changed += await EnrollmentReceivableRepricer.RepriceUnpaidAsync(enrollment, db, null, now);
        return changed;
    }
}
