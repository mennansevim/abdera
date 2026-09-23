using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Tests.Integration;

// Kurs kaydı açılınca o ayın aidatı hemen açılır (EnrollmentReceivableOpener). Testler gerçek
// SystemClock ile koşuyor, yani "bu ay" testin koştuğu aya göre değişir - sabit dönem
// etiketleriyle ("2026-10") kendi aidatını kuran bir test, o aya gelindiğinde otomatik açılan
// satırla çakışırdı.
internal static class TestPeriods
{
    // Aidat dönemlerini kendisi kuran testler kaydı bu tarihte başlatır: başlangıcı ileri bir
    // ayda olan kayıt bu ayın aidatını doğurmaz. Aidat fiyatlaması, aylık üretim, peşin ödeme ve
    // banka eşleştirmesi başlangıç tarihine bakmaz; yalnızca otomatik açılış bakar.
    public static readonly DateOnly StartsInLaterPeriod = new(2099, 1, 1);

    // Okulun yerel takvimindeki bu ay - MonthlyReceivableGenerator/EnrollmentReceivableOpener ile aynı hesap.
    public static string Current(IServiceProvider services)
    {
        var clock = services.GetRequiredService<IClock>();
        var today = clock.ToSchoolLocal(clock.UtcNow);
        return BillingPeriod.Format(today.Year, today.Month);
    }
}
