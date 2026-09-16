using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Domain;

// Tek satırlık aidat politikası (NotificationAutomationSettings ile aynı singleton deseni).
// İndirimler ayrı bir tabloya/entity'ye değil buraya konuldu: okulun kuralı "2 kursa
// katılanlar & kardeşler için %5" gibi kurum geneli bir politika - öğrenci başına
// saklanacak bir veri değil, aidat üretilirken uygulanacak bir kural.
public class BillingSettings
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    public Guid Id { get; private set; }
    // "2 kursa katılanlar için %5"
    public decimal MultiCourseDiscountPercent { get; private set; } = 5m;
    // "kardeşler için %5"
    public decimal SiblingDiscountPercent { get; private set; } = 5m;
    // Aidatın vade günü (ayın kaçı). Eskiden her ücret planında ayrı ayrı sorulan
    // DueDay alanıydı - okul genelinde tek bir gün yeterli, plan başına sormak
    // gereksiz bir soruydu. 1-28: şubat dahil her ayda var olan bir gün.
    public int DueDayOfMonth { get; private set; } = 1;
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private BillingSettings() { }

    public static BillingSettings CreateDefault(DateTimeOffset now) => new()
    {
        Id = SingletonId,
        MultiCourseDiscountPercent = 5m,
        SiblingDiscountPercent = 5m,
        DueDayOfMonth = 1,
        UpdatedAt = now,
    };

    // Satır yoksa kalıcı olmayan bir varsayılan döner - salt okuma çağrısı Add/Save
    // tetiklemez (NotificationAutomationSettings.GetCurrentAsync ile aynı gerekçe).
    public static async Task<BillingSettings> GetCurrentAsync(AbderaDbContext db) =>
        await db.BillingSettings.SingleOrDefaultAsync(s => s.Id == SingletonId)
        ?? CreateDefault(DateTimeOffset.MinValue);

    public void Update(
        decimal multiCourseDiscountPercent, decimal siblingDiscountPercent, int dueDayOfMonth,
        Guid? updatedBy, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (multiCourseDiscountPercent is < 0 or > 100)
            errors["multiCourseDiscountPercent"] = ["İndirim yüzdesi 0 ile 100 arasında olmalı."];
        if (siblingDiscountPercent is < 0 or > 100)
            errors["siblingDiscountPercent"] = ["İndirim yüzdesi 0 ile 100 arasında olmalı."];
        if (dueDayOfMonth is < 1 or > 28)
            errors["dueDayOfMonth"] = ["Vade günü 1 ile 28 arasında olmalı."];
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        MultiCourseDiscountPercent = multiCourseDiscountPercent;
        SiblingDiscountPercent = siblingDiscountPercent;
        DueDayOfMonth = dueDayOfMonth;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
