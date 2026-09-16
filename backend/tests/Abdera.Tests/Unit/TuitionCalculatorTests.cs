using Abdera.Api.Modules.Billing.Domain;

namespace Abdera.Tests.Unit;

// Aidat hesabının tamamı - CLAUDE.md test stratejisi: "ücret hesaplama -> saf birim testi".
// Okulun velilere duyurduğu gerçek rakamlar üzerinden kurgulandı: Birebir 6.000, Grup 4.500,
// 2 kurs & kardeş %5, toplu ödemede kademeli indirim.
public class TuitionCalculatorTests
{
    private static TuitionCalculator.DiscountContext Context(
        bool multiCourse = false, bool sibling = false, decimal? manual = null, string? manualReason = null) =>
        new(multiCourse, sibling, manual, manualReason, MultiCoursePercent: 5m, SiblingPercent: 5m);

    [Fact]
    public void No_discount_leaves_amount_untouched()
    {
        var result = TuitionCalculator.ComputeMonthly(6000m, Context());

        Assert.Equal(6000m, result.BaseAmount);
        Assert.Equal(0m, result.DiscountPercent);
        Assert.Null(result.DiscountReason);
        Assert.Equal(6000m, result.NetAmount);
    }

    [Fact]
    public void Multi_course_discount_applies_five_percent()
    {
        var result = TuitionCalculator.ComputeMonthly(6000m, Context(multiCourse: true));

        Assert.Equal(5m, result.DiscountPercent);
        Assert.Equal(5700m, result.NetAmount);
        Assert.Equal("2 kurs indirimi (%5)", result.DiscountReason);
    }

    [Fact]
    public void Sibling_discount_applies_to_group_rate_too()
    {
        var result = TuitionCalculator.ComputeMonthly(4500m, Context(sibling: true));

        Assert.Equal(4275m, result.NetAmount);
        Assert.Equal("Kardeş indirimi (%5)", result.DiscountReason);
    }

    // Kullanıcının kararı: indirimler TOPLANMAZ, en yükseği uygulanır.
    [Fact]
    public void Multi_course_and_sibling_together_take_the_highest_not_the_sum()
    {
        var result = TuitionCalculator.ComputeMonthly(6000m, Context(multiCourse: true, sibling: true));

        Assert.Equal(5m, result.DiscountPercent);
        Assert.Equal(5700m, result.NetAmount); // %10 olsaydı 5.400 olurdu
    }

    [Fact]
    public void Highest_wins_when_percentages_differ()
    {
        var context = new TuitionCalculator.DiscountContext(
            AttendsMultipleCourses: true, HasSibling: true, ManualPercent: null, ManualReason: null,
            MultiCoursePercent: 5m, SiblingPercent: 12m);

        var result = TuitionCalculator.ComputeMonthly(6000m, context);

        Assert.Equal(12m, result.DiscountPercent);
        Assert.Equal("Kardeş indirimi (%12)", result.DiscountReason);
    }

    // Elle girilen indirim otomatik kuralın YERİNE geçer - daha düşük olsa bile.
    [Fact]
    public void Manual_discount_overrides_automatic_rules_even_when_lower()
    {
        var result = TuitionCalculator.ComputeMonthly(
            6000m, Context(multiCourse: true, sibling: true, manual: 3m, manualReason: "Burslu öğrenci"));

        Assert.Equal(3m, result.DiscountPercent);
        Assert.Equal(5820m, result.NetAmount);
        Assert.Equal("Burslu öğrenci (%3)", result.DiscountReason);
    }

    [Fact]
    public void Manual_discount_without_reason_still_explains_itself()
    {
        var result = TuitionCalculator.ComputeMonthly(6000m, Context(manual: 7.5m));

        Assert.Equal("Özel indirim (%7,5)", result.DiscountReason);
        Assert.Equal(5550m, result.NetAmount);
    }

    // Peşin ödeme indirimi öğrenci indiriminin ÜSTÜNE biner ve bileşik hesaplanır:
    // %5 ve %10 birlikte %15 değil %14,5 eder.
    [Fact]
    public void Prepay_discount_compounds_with_student_discount()
    {
        var result = TuitionCalculator.ComputePrepaidMonthly(6000m, Context(multiCourse: true), prepayPercent: 10m);

        Assert.Equal(14.5m, result.DiscountPercent);
        Assert.Equal(5130m, result.NetAmount); // 6000 * 0,95 * 0,90
        Assert.Equal("2 kurs indirimi (%5) + Peşin ödeme indirimi (%10)", result.DiscountReason);
    }

    [Fact]
    public void Prepay_discount_alone_is_reported_without_student_reason()
    {
        var result = TuitionCalculator.ComputePrepaidMonthly(6000m, Context(), prepayPercent: 10m);

        Assert.Equal(10m, result.DiscountPercent);
        Assert.Equal(5400m, result.NetAmount);
        Assert.Equal("Peşin ödeme indirimi (%10)", result.DiscountReason);
    }

    [Fact]
    public void Zero_prepay_percent_falls_back_to_plain_monthly()
    {
        var prepaid = TuitionCalculator.ComputePrepaidMonthly(4500m, Context(sibling: true), prepayPercent: 0m);
        var monthly = TuitionCalculator.ComputeMonthly(4500m, Context(sibling: true));

        Assert.Equal(monthly, prepaid);
    }

    [Theory]
    [InlineData(1, 0)]     // tek ay peşin sayılmaz
    [InlineData(3, 0)]     // en düşük kademenin altında
    [InlineData(4, 5)]     // ilk kademe
    [InlineData(9, 5)]     // hâlâ ilk kademe
    [InlineData(10, 10)]   // tüm sezon
    [InlineData(12, 10)]   // üst kademe kalır
    public void Prepay_tier_resolves_to_highest_matching_step(int months, decimal expected)
    {
        PrepayDiscountTier[] tiers = [PrepayDiscountTier.Create(4, 5m), PrepayDiscountTier.Create(10, 10m)];

        Assert.Equal(expected, TuitionCalculator.ResolvePrepayPercent(tiers, months));
    }

    [Fact]
    public void Prepay_percent_is_zero_when_no_tier_defined()
    {
        Assert.Equal(0m, TuitionCalculator.ResolvePrepayPercent([], 12));
    }

    // Kuruş hassasiyeti: %3 gibi tek sayılı bir oranda yuvarlama iki ondalıkta durmalı.
    [Fact]
    public void Net_amount_is_rounded_to_two_decimals()
    {
        var result = TuitionCalculator.ComputeMonthly(4333.33m, Context(manual: 3m));

        Assert.Equal(4203.33m, result.NetAmount);
    }

    [Fact]
    public void Free_enrollment_is_expressible_as_hundred_percent()
    {
        var result = TuitionCalculator.ComputeMonthly(6000m, Context(manual: 100m, manualReason: "Personel çocuğu"));

        Assert.Equal(0m, result.NetAmount);
        Assert.Equal("Personel çocuğu (%100)", result.DiscountReason);
    }
}
