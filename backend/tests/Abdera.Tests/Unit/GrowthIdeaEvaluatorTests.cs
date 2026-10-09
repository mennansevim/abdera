using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Profitability.Domain;

namespace Abdera.Tests.Unit;

// Kârlılık ekranının fikir değerlendiricisi: saf hesap, veritabanı yok.
// Taban: birebir 6.000, grup 4.500, ayda 4 ders, indirim yok, ders başı ücret 500, kalış 9 ay.
public class GrowthIdeaEvaluatorTests
{
    private static readonly Guid Piano = Guid.NewGuid();
    private static readonly Guid Guitar = Guid.NewGuid();

    private static ProfitBaseline Baseline(decimal? rate = 500m, decimal tenure = 9m) => new(
        IndividualPrice: 6000m,
        GroupPrice: 4500m,
        LessonsPerMonth: 4,
        AverageDiscountRate: 0m,
        AverageTeacherRate: rate,
        MonthlyRevenue: 60000m,
        ActiveEnrollments: 10,
        AverageTenureMonths: tenure,
        TenureIsDefault: false,
        Instruments:
        [
            // Piyano: 40 saat müsait, 8 saat dolu -> %20.
            new InstrumentStats(Piano, "Piyano", 8, 0, 2, 500m, BookedHours: 8m, AvailableHours: 40m),
            // Gitar: 20 saat müsait, 19 saat dolu -> %95.
            new InstrumentStats(Guitar, "Gitar", 19, 0, 1, 500m, BookedHours: 19m, AvailableHours: 20m),
        ]);

    private static GrowthIdeaParameters P => GrowthIdeaParameters.None;

    [Fact]
    public void Individual_margin_is_price_after_discount_minus_monthly_lesson_pay()
    {
        Assert.Equal(4000m, Baseline().MarginPerStudent(CourseKind.Individual, 500m));
        Assert.Equal(2500m, Baseline().MarginPerStudent(CourseKind.Group, 500m));
    }

    [Fact]
    public void New_teacher_where_existing_teachers_are_mostly_idle_is_not_recommended()
    {
        var result = GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.NewTeacher, P with { InstrumentId = Piano, Students = 3 }, Baseline());

        Assert.Equal(IdeaVerdict.NotRecommended, result.Verdict);
        Assert.Contains("talebi", result.Reasons[0]);
        // Rakam yine hesaplanır: 3 öğrenci × 4.000.
        Assert.Equal(12000m, result.MonthlyNet);
    }

    [Fact]
    public void New_teacher_where_existing_teachers_are_full_is_recommended()
    {
        var result = GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.NewTeacher, P with { InstrumentId = Guitar, Students = 3 }, Baseline());

        Assert.Equal(IdeaVerdict.Recommended, result.Verdict);
        // Öğrenciler 3 ayda kademeli gelir: ilk ay üçte biri.
        Assert.Equal(4000m, result.Series[0]);
        Assert.Equal(12000m, result.Series[2]);
    }

    [Fact]
    public void Group_class_pays_the_teacher_per_student_and_still_beats_individual_per_teacher_hour()
    {
        var result = GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.GroupClass, P with { InstrumentId = Piano, Students = 4 }, Baseline());

        // 4 × (4.500 - 4 × 500) = 10.000; öğretmen saati başına 2.500, birebirde 1.000.
        Assert.Equal(10000m, result.MonthlyNet);
        Assert.Equal(IdeaVerdict.Recommended, result.Verdict);
        Assert.Contains(result.Reasons, reason => reason.Contains("her öğrenci için ayrı ders ücreti"));
    }

    [Fact]
    public void Group_class_whose_fee_does_not_cover_lesson_pay_is_not_recommended()
    {
        var result = GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.GroupClass,
            P with { InstrumentId = Piano, Students = 4, TeacherRatePerLesson = 1500m }, Baseline());

        Assert.True(result.MonthlyNet < 0);
        Assert.Equal(IdeaVerdict.NotRecommended, result.Verdict);
    }

    [Fact]
    public void New_branch_is_uncertain_until_demand_is_measured()
    {
        var result = GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.NewBranch,
            P with { BranchName = "İngilizce", CourseKind = CourseKind.Individual, Students = 5, TeacherRatePerLesson = 600m }, Baseline());

        Assert.Equal(IdeaVerdict.Uncertain, result.Verdict);
        // 5 × (6.000 - 4 × 600).
        Assert.Equal(18000m, result.MonthlyNet);
    }

    [Fact]
    public void Incentive_counts_the_discount_given_to_students_who_would_have_come_anyway()
    {
        var withoutLeak = GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.Incentive,
            P with { Students = 4, DiscountPercent = 30m, DiscountMonths = 3, AlreadyComingPercent = 0 }, Baseline());
        var withLeak = GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.Incentive,
            P with { Students = 4, DiscountPercent = 30m, DiscountMonths = 3, AlreadyComingPercent = 50 }, Baseline());

        Assert.True(withLeak.TwelveMonthNet < withoutLeak.TwelveMonthNet);
        Assert.Contains(withLeak.Reasons, reason => reason.StartsWith("Zaten gelecek öğrencilere verilen indirim"));
    }

    [Fact]
    public void Incentive_where_everyone_would_have_come_anyway_loses_money()
    {
        var result = GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.Incentive,
            P with { Students = 4, DiscountPercent = 50m, DiscountMonths = 6, AlreadyComingPercent = 100 }, Baseline());

        Assert.True(result.TwelveMonthNet < 0);
        Assert.Equal(IdeaVerdict.NotRecommended, result.Verdict);
    }

    [Fact]
    public void Price_change_starts_next_month_and_reports_break_even_student_loss()
    {
        var result = GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.PriceChange, P with { PriceChangePercent = 10m, LostStudents = 1 }, Baseline());

        Assert.Equal(0m, result.Series[0]);
        // 60.000 × %10 = 6.000 ek gelir - 1 öğrenci × (6.600 - 2.000) = 1.400.
        Assert.Equal(1400m, result.MonthlyNet);
        Assert.Equal(IdeaVerdict.Recommended, result.Verdict);
        Assert.Contains("1 öğrenciden fazlası", result.Reasons[0]);
    }

    [Fact]
    public void One_time_cost_hits_the_first_month_and_sets_the_payback_month()
    {
        var result = GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.Custom, P with { MonthlyAmount = 1000m, OneTimeCost = 2500m }, Baseline());

        Assert.Equal(-1500m, result.Series[0]);
        Assert.Equal(3, result.PaybackMonth);
        Assert.Equal(12000m - 2500m, result.TwelveMonthNet);
    }

    [Fact]
    public void Missing_teacher_rates_are_called_out_instead_of_silently_inflating_profit()
    {
        var result = GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.NewBranch,
            P with { BranchName = "İngilizce", CourseKind = CourseKind.Group, Students = 4 },
            Baseline(rate: null) with { Instruments = [] });

        Assert.Contains(result.Reasons, reason => reason.Contains("ücretleri girilmemiş"));
    }

    [Fact]
    public void Validation_rejects_a_one_person_group_and_parameters_of_another_kind_are_dropped()
    {
        var errors = GrowthIdea.Validate(GrowthIdeaKind.GroupClass, "Grup", null, P with { InstrumentId = Piano, Students = 1 });
        Assert.True(errors.ContainsKey("students"));

        var idea = GrowthIdea.Create(GrowthIdeaKind.PriceChange, "Zam", null,
            P with { PriceChangePercent = 5m, LostStudents = 0, Students = 7, InstrumentId = Piano }, null, DateTimeOffset.UtcNow);
        Assert.Null(idea.Students);
        Assert.Null(idea.InstrumentId);
        Assert.Equal(5m, idea.PriceChangePercent);
    }

    [Fact]
    public void Commentary_facts_carry_no_person_names_and_stay_short()
    {
        var baseline = Baseline();
        var snapshot = new ProfitSnapshot("2026-10",
            [new MonthProfit("2026-09", 60000m, 20000m, 15000m, false, false), new MonthProfit("2026-10", 62000m, 21000m, 15000m, true, false)],
            baseline, 3, 2, [new OperationGap("unmarked_lessons", GapSeverity.Warning, 4, "Son 14 günde 4 dersin yoklaması girilmemiş.")]);
        var insights = ProfitInsights.Build(snapshot);

        var facts = ProfitCommentaryFacts.Build(snapshot, insights,
            [new ProfitCommentaryFacts.IdeaFact("Gitar grubu", GrowthIdeaKind.GroupClass, GrowthIdeaStatus.Idea,
                GrowthIdeaEvaluator.Evaluate(GrowthIdeaKind.GroupClass, P with { InstrumentId = Piano, Students = 4 }, baseline))]);

        Assert.Contains("2026-10", facts);
        // Yön modele hazır verilir: 21.000 -> 26.000 = 1.000 fazla.
        Assert.Contains("geçen aydan ₺1.000 FAZLA (arttı)", facts);
        Assert.Contains("Gitar grubu", facts);
        Assert.Contains("yoklaması girilmemiş", facts);
        Assert.Contains(insights, insight => insight.Contains("ilk 3 ayındaydı"));
        Assert.True(facts.Length < 4000, $"Olgu metni beklenenden uzun: {facts.Length}");
    }

    [Fact]
    public void Commentary_cannot_be_refreshed_beyond_the_monthly_limit()
    {
        var commentary = ProfitCommentary.Create("2026-10", "İlk yorum", "test", DateTimeOffset.UtcNow);
        for (var i = 0; i < ProfitCommentary.MaxRefreshesPerMonth; i++)
            commentary.Refresh($"Yorum {i}", "test", DateTimeOffset.UtcNow);

        Assert.False(commentary.CanRefresh);
        Assert.Throws<Abdera.Api.Shared.ConflictException>(() => commentary.Refresh("Fazla", "test", DateTimeOffset.UtcNow));
    }
}
