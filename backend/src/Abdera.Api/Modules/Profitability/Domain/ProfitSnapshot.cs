using System.Globalization;
using System.Text;
using Abdera.Api.Modules.People.Domain;

namespace Abdera.Api.Modules.Profitability.Domain;

// Bir ayın tahakkuk bazlı kârı. Gelir = o dönemin iptal edilmemiş aidatları (tahsilat değil:
// veliler düzenli ödüyor, kârlılığın sorusu tahsilat değil - kullanıcı kararı, V2).
// Öğretmen maliyeti = tamamlanan dersler (içinde bulunulan ayda planlı dersler de) × ders başı
// ücret. Sabit gider = tekrarlayan kalemler + tek seferlik giderler; haftalık öğretmen ödemesinin
// giderlere yazılan Maaş satırları hariç, yoksa öğretmen maliyeti iki kez düşülürdü.
public record MonthProfit(
    string Period,
    decimal Revenue,
    decimal TeacherCost,
    decimal FixedCost,
    bool IsCurrent,
    // Ücreti girilmemiş öğretmenin dersi okul ortalamasıyla (ya da 0) sayıldıysa true.
    bool TeacherCostEstimated)
{
    public decimal Net => Revenue - TeacherCost - FixedCost;
}

public enum GapSeverity
{
    Warning,
    Info,
}

public record OperationGap(string Key, GapSeverity Severity, int Count, string Message);

public record ProfitSnapshot(
    string Period,
    IReadOnlyList<MonthProfit> Months,
    ProfitBaseline Baseline,
    int EndedLast3Months,
    int EarlyChurnLast3Months,
    IReadOnlyList<OperationGap> Gaps)
{
    public MonthProfit Current => Months[^1];
    public MonthProfit? Previous => Months.Count > 1 ? Months[^2] : null;
}

// Kural tabanlı gözlemler: yapay zekâ kapalıyken de ekran tavsiye verebilsin, açıkken de
// modele hazır sonuç gitsin (model hesap yapmaz, yalnızca yorumlar).
public static class ProfitInsights
{
    public const int GroupSizeForComparison = 4;
    public const int MaxInsights = 4;

    public static IReadOnlyList<string> Build(ProfitSnapshot snapshot)
    {
        var b = snapshot.Baseline;
        var insights = new List<string>();
        var rate = b.AverageTeacherRate ?? 0;

        if (b.LessonsPerMonth > 0 && b.IndividualPrice > 0)
        {
            var individualHour = b.MarginPerStudent(CourseKind.Individual, rate) / b.LessonsPerMonth;
            var groupHour = GroupSizeForComparison * b.MarginPerStudent(CourseKind.Group, rate) / b.LessonsPerMonth;
            insights.Add(groupHour > individualHour
                ? $"Bir saatlik {GroupSizeForComparison} kişilik grup dersi {Tl.Format(groupHour)}, bir saatlik birebir ders {Tl.Format(individualHour)} kazandırıyor. Boş saati olan branşta grup açmak en kârlı adım."
                : $"Grup dersi saat başına birebirden az kazandırıyor ({Tl.Format(groupHour)} / {Tl.Format(individualHour)}), çünkü öğretmene grupta her öğrenci için ayrı ücret ödeniyor.");
        }

        var measured = b.Instruments.Where(row => row.Teachers > 0 && row.Utilization is not null).ToList();
        var idle = measured.Where(row => row.Utilization < GrowthIdeaEvaluator.LowUtilization).OrderBy(row => row.Utilization).FirstOrDefault();
        if (idle is not null)
            insights.Add($"{idle.Name} öğretmenlerinin haftada {Round(idle.SpareHours)} saati boş. Yeni öğretmen almak yerine bu saatlere öğrenci bul.");
        var busy = measured.Where(row => row.Utilization >= GrowthIdeaEvaluator.HighUtilization).OrderByDescending(row => row.Utilization).FirstOrDefault();
        if (busy is not null)
            insights.Add($"{busy.Name} öğretmenlerinin programı dolmak üzere ({Tl.Percent(busy.Utilization!.Value)}). Yeni öğrenci gelirse ek öğretmen gerekecek.");

        if (snapshot.EarlyChurnLast3Months >= 2)
            insights.Add($"Son 3 ayda ayrılan {snapshot.EndedLast3Months} kaydın {snapshot.EarlyChurnLast3Months}'i ilk 3 ayındaydı. Yeni gelen veliyle ilk ay kısa bir görüşme öğrenciyi daha uzun tutar.");
        else if (b.IndividualPrice > 0)
            insights.Add($"Bir öğrenci ortalama {Round(b.AverageTenureMonths)} ay kalıyor ve bu sürede okula {Tl.Format(b.LifetimeValue(rate))} bırakıyor. Bir öğrenciyi kaybetmek bu kadar para demek.");

        return insights.Take(MaxInsights).ToList();
    }

    private static string Round(decimal? value) =>
        decimal.Round(value ?? 0, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
}

// Modele gidecek kısa olgu metni. Kişi adı (öğrenci, veli, öğretmen) GİTMEZ - yalnızca toplu
// sayılar, enstrüman adları ve fikir başlıkları. Girdi ~1.000-1.500 token'la sınırlı kalsın
// diye listeler kırpılır.
public static class ProfitCommentaryFacts
{
    public const int MaxIdeas = 8;
    public const int MaxInstruments = 10;

    public record IdeaFact(string Title, GrowthIdeaKind Kind, GrowthIdeaStatus Status, GrowthIdeaEvaluation Evaluation);

    public static string Build(ProfitSnapshot snapshot, IReadOnlyList<string> insights, IReadOnlyList<IdeaFact> ideas)
    {
        var b = snapshot.Baseline;
        var text = new StringBuilder();
        text.Append("Dönem: ").AppendLine(snapshot.Period);
        // Yön ve fark burada hesaplanır: model rakamları kıyaslarken yanılabiliyor (canlı denemede
        // ₺523 artışa "değişmedi" dedi), yorum bu satırı aynen kullanır.
        text.Append("ÖZET: Bu ay cebinde kalan ").Append(Tl.Format(snapshot.Current.Net));
        if (snapshot.Previous is { } previous)
        {
            var change = snapshot.Current.Net - previous.Net;
            text.Append("; geçen ay ").Append(Tl.Format(previous.Net)).Append("; ")
                .Append(change switch
                {
                    > 0 => $"geçen aydan {Tl.Format(change)} FAZLA (arttı)",
                    < 0 => $"geçen aydan {Tl.Format(-change)} AZ (azaldı)",
                    _ => "geçen ayla aynı",
                });
        }
        text.AppendLine(".");
        if (b.AverageTeacherRate is null)
            text.AppendLine("ÖNEMLİ (bu cümleyi aynen kullan): \"Öğretmen ücretleri girilmediği için kâr olduğundan yüksek görünüyor.\"");
        text.AppendLine("Aylık kâr (gelir / öğretmen / sabit gider / net):");
        foreach (var month in snapshot.Months)
        {
            text.Append("- ").Append(month.Period).Append(month.IsCurrent ? " (bu ay, sürüyor)" : "").Append(": ")
                .Append(Tl.Format(month.Revenue)).Append(" / ").Append(Tl.Format(month.TeacherCost))
                .Append(" / ").Append(Tl.Format(month.FixedCost)).Append(" / ").AppendLine(Tl.Format(month.Net));
        }

        var rate = b.AverageTeacherRate ?? 0;
        text.AppendLine("Birim ekonomisi:");
        text.Append("- Aktif kurs kaydı: ").AppendLine(b.ActiveEnrollments.ToString(CultureInfo.InvariantCulture));
        text.Append("- Aylık aidat: birebir ").Append(Tl.Format(b.IndividualPrice)).Append(", grup ").Append(Tl.Format(b.GroupPrice))
            .Append(" (").Append(b.LessonsPerMonth.ToString(CultureInfo.InvariantCulture)).AppendLine(" ders)");
        text.Append("- Ortalama indirim: ").AppendLine(Tl.Percent(b.AverageDiscountRate));
        text.Append("- Ortalama öğretmen ders ücreti: ").AppendLine(b.AverageTeacherRate is { } avg ? Tl.Format(avg) : "girilmemiş");
        text.Append("- Birebir öğrenci başı aylık katkı: ").AppendLine(Tl.Format(b.MarginPerStudent(CourseKind.Individual, rate)));
        text.Append("- Ortalama kalış: ").Append(decimal.Round(b.AverageTenureMonths, 1).ToString(CultureInfo.InvariantCulture))
            .AppendLine(b.TenureIsDefault ? " ay (varsayılan, veri az)" : " ay");
        text.Append("- Son 3 ayda biten kayıt: ").Append(snapshot.EndedLast3Months.ToString(CultureInfo.InvariantCulture))
            .Append(", bunların ilk 3 ayında bitenleri: ").AppendLine(snapshot.EarlyChurnLast3Months.ToString(CultureInfo.InvariantCulture));

        text.AppendLine("Branşlar (birebir / grup / öğretmen / doluluk / boş saat):");
        foreach (var row in b.Instruments.Where(row => row.Teachers > 0 || row.Individual + row.Group > 0).Take(MaxInstruments))
        {
            text.Append("- ").Append(row.Name).Append(": ").Append(row.Individual).Append(" / ").Append(row.Group)
                .Append(" / ").Append(row.Teachers).Append(" / ")
                .Append(row.Utilization is { } utilization ? Tl.Percent(utilization) : "bilinmiyor").Append(" / ")
                .AppendLine(row.SpareHours is { } spare ? decimal.Round(spare, 0).ToString(CultureInfo.InvariantCulture) : "bilinmiyor");
        }

        if (snapshot.Gaps.Count > 0)
        {
            text.AppendLine("İşleyişteki eksikler:");
            foreach (var gap in snapshot.Gaps) text.Append("- ").AppendLine(gap.Message);
        }
        if (insights.Count > 0)
        {
            text.AppendLine("Hesaplanmış gözlemler:");
            foreach (var insight in insights) text.Append("- ").AppendLine(insight);
        }

        var shown = ideas.Where(idea => idea.Status != GrowthIdeaStatus.Dropped).Take(MaxIdeas).ToList();
        if (shown.Count > 0)
        {
            text.AppendLine("Yöneticinin fikirleri (başlık / durum / aylık net / 12 ay / değerlendirme):");
            foreach (var idea in shown)
            {
                text.Append("- ").Append(Clip(idea.Title, 80)).Append(" / ").Append(StatusLabel(idea.Status))
                    .Append(" / ").Append(Tl.Format(idea.Evaluation.MonthlyNet)).Append(" / ").Append(Tl.Format(idea.Evaluation.TwelveMonthNet))
                    .Append(" / ").AppendLine(VerdictLabel(idea.Evaluation.Verdict));
            }
        }
        return text.ToString();
    }

    private static string StatusLabel(GrowthIdeaStatus status) => status switch
    {
        GrowthIdeaStatus.Trying => "deneniyor",
        GrowthIdeaStatus.Applied => "uygulandı",
        GrowthIdeaStatus.Dropped => "vazgeçildi",
        _ => "fikir",
    };

    private static string VerdictLabel(IdeaVerdict verdict) => verdict switch
    {
        IdeaVerdict.Recommended => "önerilen",
        IdeaVerdict.NotRecommended => "önerilmez",
        _ => "belirsiz",
    };

    private static string Clip(string value, int max) => value.Length > max ? value[..max] + "…" : value;
}
