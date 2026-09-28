namespace Abdera.Api.Modules.Billing.Domain;

// Bir öğretmene bir ödeme haftası için yapılan ödeme (docs/10-decisions.md O1).
//
// Fiyat snapshot'ı kuralının (CLAUDE.md) öğretmen tarafındaki karşılığı: hesabın TAMAMI
// satıra donar - kaç ders (`LessonCount`), ders başı kaç para (`RatePerLesson`), bunun
// çarpımı (`ComputedAmount`) ve gerçekte ödenen (`Amount`). Sonraki bir ücret değişikliği
// ödenmiş haftaları değiştirmez ve "bu tutar nereden geldi" sorusu başka tabloya gitmeden
// yanıtlanır.
//
// Satır silinmez: ödeme gider defterine de yazıldığı için (`ExpenseId`) kaydın kendisi
// finansal bir iz. Yanlış girilen bir ödeme, giderlerin geri kalanında olduğu gibi, karşı
// bir kayıtla düzeltilir.
public class TeacherWeeklyPayout
{
    public Guid Id { get; private set; }
    public Guid TeacherId { get; private set; }
    public DateOnly WeekStart { get; private set; }
    public DateOnly WeekEnd { get; private set; }
    public int LessonCount { get; private set; }
    public decimal RatePerLesson { get; private set; }
    public decimal ComputedAmount { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public DateOnly PaidOn { get; private set; }
    public string? Note { get; private set; }
    public Guid ExpenseId { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private TeacherWeeklyPayout() { }

    public static TeacherWeeklyPayout Create(
        Guid teacherId,
        TeacherPayWeek week,
        int lessonCount,
        decimal ratePerLesson,
        decimal computedAmount,
        decimal amount,
        string currency,
        DateOnly paidOn,
        string? note,
        Guid expenseId,
        Guid? createdBy,
        DateTimeOffset now)
    {
        if (lessonCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(lessonCount), "Ödeme için en az bir tamamlanmış ders gerekir.");
        if (ratePerLesson <= 0)
            throw new ArgumentOutOfRangeException(nameof(ratePerLesson), "Ders başı ücret pozitif olmalı.");
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Ödeme tutarı pozitif olmalı.");

        return new TeacherWeeklyPayout
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            WeekStart = week.Start,
            WeekEnd = week.End,
            LessonCount = lessonCount,
            RatePerLesson = ratePerLesson,
            ComputedAmount = computedAmount,
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.Trim().ToUpperInvariant(),
            PaidOn = paidOn,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ExpenseId = expenseId,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
