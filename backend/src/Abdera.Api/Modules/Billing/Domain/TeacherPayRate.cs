namespace Abdera.Api.Modules.Billing.Domain;

// Öğretmenin ders başına ücreti (docs/10-decisions.md N1). Öğretmen başına TEK satır:
// tuition_rates'teki gibi tarihli sürümleme yok, çünkü geçmişi koruyan şey haftalık ödemenin
// kendi satırındaki snapshot (rate_per_lesson + lesson_count + computed_amount). Ücret
// değiştiğinde yalnızca bundan sonraki ödemeler etkilenir; ödenmiş haftalar kendi tutarını
// taşımaya devam eder. Eski/yeni değer audit_log'a yazılır.
public class TeacherPayRate
{
    public Guid Id { get; private set; }
    public Guid TeacherId { get; private set; }
    public decimal AmountPerLesson { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TeacherPayRate() { }

    public static TeacherPayRate Create(
        Guid teacherId, decimal amountPerLesson, string currency, Guid? actorId, DateTimeOffset now)
    {
        EnsureValidAmount(amountPerLesson);
        return new TeacherPayRate
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            AmountPerLesson = amountPerLesson,
            Currency = NormalizeCurrency(currency),
            UpdatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void ChangeAmount(decimal amountPerLesson, string currency, Guid? actorId, DateTimeOffset now)
    {
        EnsureValidAmount(amountPerLesson);
        AmountPerLesson = amountPerLesson;
        Currency = NormalizeCurrency(currency);
        UpdatedBy = actorId;
        UpdatedAt = now;
    }

    // Bir haftanın ham tutarı. Kuruş yuvarlaması burada yapılır ki hesabın tek bir yeri olsun
    // (CLAUDE.md "hesabın tek yeri" kuralının bu modüldeki karşılığı).
    public decimal ComputeWeeklyAmount(int completedLessons)
    {
        if (completedLessons < 0)
            throw new ArgumentOutOfRangeException(nameof(completedLessons), "Ders sayısı negatif olamaz.");

        return decimal.Round(AmountPerLesson * completedLessons, 2, MidpointRounding.AwayFromZero);
    }

    private static void EnsureValidAmount(decimal amountPerLesson)
    {
        if (amountPerLesson <= 0)
            throw new ArgumentException("Ders başı ücret pozitif olmalı.", nameof(amountPerLesson));
    }

    private static string NormalizeCurrency(string currency) =>
        string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.Trim().ToUpperInvariant();
}
