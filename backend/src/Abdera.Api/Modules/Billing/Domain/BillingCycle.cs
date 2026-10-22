using Abdera.Api.Modules.People.Domain;

namespace Abdera.Api.Modules.Billing.Domain;

// Dönem (4 ders) tabanlı aidat - docs/10-decisions.md AD. Birebir kurs kaydında aidat takvim
// ayına göre değil yoklama kartına göre açılır: Geldi ya da Mazeretli işaretlenen her ders
// kartın bir satırını doldurur, 4 satır bir dönemdir. Dönemin ilk dersi işlendiğinde o dönemin
// aidatı tahakkuk eder - yani "4 ders doldu, sonraki derste yeni aidat".
//
// Dönem aidatı `receivables` tablosunda aylık aidatla aynı satır tipidir; `period` kolonunda
// "2026-10" yerine dönem anahtarı ("D001") taşır. Böylece UNIQUE (enrollment_id, period)
// dönemin çift tahakkukunu da engeller, tahsilat ve ödeme penceresi aynı yolu kullanır.
public static class BillingCycle
{
    public const int LessonsPerCycle = 4;
    // Dönem aidatının vadesi tahakkuk gününden bu kadar gün sonradır.
    public const int DueDays = 7;
    private const char Prefix = 'D';

    public static string Key(int number)
    {
        if (number is < 1 or > 999) throw new ArgumentOutOfRangeException(nameof(number), "Dönem numarası 1-999 arasında olmalı.");
        return $"{Prefix}{number:D3}";
    }

    public static bool IsKey(string? period) =>
        period is { Length: 4 } && period[0] == Prefix && int.TryParse(period.AsSpan(1), out var number) && number >= 1;

    public static int Number(string key) =>
        IsKey(key) ? int.Parse(key.AsSpan(1)) : throw new ArgumentException($"'{key}' bir dönem anahtarı değil.", nameof(key));

    // Veliye giden metinde "{label} dönemi aidatı" kalıbına oturan etiket: "2026-10" ya da "3.".
    public static string MessageLabel(string period) => IsKey(period) ? $"{Number(period)}." : period;

    // Gelir/kârlılık raporlarında aidatın düştüğü ay: aylık aidatta kendi dönemi, dönem aidatında
    // tahakkuk gününün ayı. Başlamamış (peşin) dönem henüz hiçbir aya düşmez (null).
    public static string? AccrualMonth(string period, DateOnly? accruedOn) =>
        !IsKey(period) ? period : accruedOn is { } on ? BillingPeriod.Format(on.Year, on.Month) : null;

    // Yalnızca birebir dersler dönemle faturalanır; Resim grup dersi aylık aidatta kalır
    // (grup öğretmeninin %40 payı aylık aidattan hesaplanıyor - GroupLessonPay).
    public static bool AppliesTo(CourseKind courseKind) => courseKind == CourseKind.Individual;

    // Geçiş tarihinde ya da sonrasında başlayan aylarda birebir kayda aylık aidat açılmaz.
    // Geçiş ay ortasına konursa o ayın aylık aidatı yine açılır; ayın 1'i önerilir.
    public static bool ReplacesMonthly(CourseKind courseKind, DateOnly? startsOn, string period) =>
        AppliesTo(courseKind) && startsOn is { } start && BillingPeriod.FirstDay(period) >= start;

    // Kaç dönemin tahakkuk etmiş olması gerektiği: 1 sayılan ders 1. dönemi açar, 5. ders 2.'yi.
    public static int RequiredCycles(int countedLessons) =>
        countedLessons <= 0 ? 0 : (countedLessons + LessonsPerCycle - 1) / LessonsPerCycle;

    // n. dönemi açan dersin sayılan dersler listesindeki sırası (0 tabanlı).
    public static int OpeningLessonIndex(int number) => (number - 1) * LessonsPerCycle;

    // Kartta bir dersin hangi dönemin satırı olduğu (1 tabanlı).
    public static int CycleOf(int countedIndex) => countedIndex / LessonsPerCycle + 1;
}
