using Abdera.Api.Modules.Assistant.Domain;

namespace Abdera.Tests.Unit;

// Asistanın kural yolu (docs/10-decisions.md AE). Cümleler mock'ta ve gerçek kullanımda
// yazılan biçimlerdir: kesmeli/kesmesiz, büyük harf, noktalı saat.
public class AssistantIntentParserTests
{
    [Theory]
    [InlineData("Mennan Sevim'in gelişimini getir", AssistantIntentKind.Progress)]
    [InlineData("MENNAN SEVİM GELİŞİM", AssistantIntentKind.Progress)]
    [InlineData("Ece nasıl gidiyor?", AssistantIntentKind.Progress)]
    [InlineData("Ece'nin dersleri nasıl gidiyor", AssistantIntentKind.Progress)]
    [InlineData("Deniz'in karnesini göster", AssistantIntentKind.Progress)]
    [InlineData("Aidat günü gelenleri listele", AssistantIntentKind.Dues)]
    [InlineData("Ödemesi geciken öğrenciler", AssistantIntentKind.Dues)]
    [InlineData("Bu hafta vadesi dolan aidatlar", AssistantIntentKind.Dues)]
    [InlineData("Mennan'ın borcu var mı?", AssistantIntentKind.Dues)]
    [InlineData("Mennan Sevim'in perşembe dersini 17:00'ye al", AssistantIntentKind.Reschedule)]
    [InlineData("Ece'nin dersini cumaya taşı", AssistantIntentKind.Reschedule)]
    [InlineData("Mennan'ın ders saatini değiştir", AssistantIntentKind.Reschedule)]
    [InlineData("yarınki dersi saat 5'e çek", AssistantIntentKind.Reschedule)]
    [InlineData("bu seferlik cuma 17 de yapalım", AssistantIntentKind.Reschedule)]
    [InlineData("Mennan'ın dersini haftaya salıya ertele", AssistantIntentKind.Reschedule)]
    [InlineData("Ece'ye telafi oluştur", AssistantIntentKind.Makeup)]
    [InlineData("Mennan'ın telafi hakkı var mı", AssistantIntentKind.Makeup)]
    [InlineData("telafi dersini cumaya koy", AssistantIntentKind.Makeup)]
    [InlineData("Mennan'ın dersleri", AssistantIntentKind.Lessons)]
    [InlineData("Ece'nin sıradaki dersi ne zaman?", AssistantIntentKind.Lessons)]
    [InlineData("Deniz'in ders programı", AssistantIntentKind.Lessons)]
    [InlineData("Mennan'ın perşembe dersini iptal et", AssistantIntentKind.Lessons)]
    [InlineData("Yoklamayı nereden alırım?", AssistantIntentKind.Navigate)]
    [InlineData("Fiyat politikası nerede?", AssistantIntentKind.Navigate)]
    [InlineData("Aidatlar nerede", AssistantIntentKind.Navigate)]
    [InlineData("Takvimi aç", AssistantIntentKind.Navigate)]
    [InlineData("Kütüphane", AssistantIntentKind.Navigate)]
    [InlineData("Ders notu nasıl yazılır", AssistantIntentKind.Navigate)]
    [InlineData("Şifremi nasıl değiştiririm", AssistantIntentKind.Navigate)]
    [InlineData("Merhaba", AssistantIntentKind.Unknown)]
    [InlineData("hava nasıl", AssistantIntentKind.Unknown)]
    [InlineData("Bu ay en çok devamsızlık yapan öğrenciler kim?", AssistantIntentKind.Unknown)]
    public void Detects_the_intent(string text, AssistantIntentKind expected)
    {
        Assert.Equal(expected, IntentParser.Parse(text).Kind);
    }

    [Theory]
    [InlineData("Yoklamayı nereden alırım?", "attendance")]
    [InlineData("Fiyat politikası nerede?", "billing-policy")]
    [InlineData("Aidatlar nerede", "billing")]
    [InlineData("Öğrenci eklemek için nereye gitmeliyim", "students-new")]
    [InlineData("Takvimi aç", "calendar")]
    [InlineData("Ders notu nasıl yazılır", "progress")]
    [InlineData("Grup dersleri nerede", "groups")]
    [InlineData("Şifremi nasıl değiştiririm", "settings")]
    public void Navigation_picks_the_most_specific_screen(string text, string expectedKey)
    {
        var parsed = IntentParser.Parse(text);

        Assert.Equal(AssistantIntentKind.Navigate, parsed.Kind);
        Assert.Equal(expectedKey, parsed.Navigation?.Key);
    }

    [Fact]
    public void Navigation_is_only_returned_for_navigate_requests()
    {
        // "aidat" bir ekran adıdır ama bu cümle liste istiyor; yönlendirme alanı boş kalır.
        Assert.Null(IntentParser.Parse("Aidat günü gelenleri listele").Navigation);
    }

    [Fact]
    public void A_day_before_the_lesson_word_is_the_source_and_a_dative_time_is_the_target()
    {
        var parsed = IntentParser.Parse("Mennan Sevim'in perşembe dersini 17:00'ye al");

        Assert.Equal(DayOfWeek.Thursday, parsed.SourceDay?.Weekday);
        Assert.Null(parsed.TargetDay);
        Assert.Null(parsed.SourceTime);
        Assert.Equal(new TimeOnly(17, 0), parsed.TargetTime);
        Assert.Equal(RescheduleScope.Unspecified, parsed.Scope);
    }

    [Fact]
    public void Source_and_target_day_and_time_are_told_apart()
    {
        var parsed = IntentParser.Parse("Deniz'in salı 16:00 dersini çarşamba 18.30'a al");

        Assert.Equal(DayOfWeek.Tuesday, parsed.SourceDay?.Weekday);
        Assert.Equal(new TimeOnly(16, 0), parsed.SourceTime);
        Assert.Equal(DayOfWeek.Wednesday, parsed.TargetDay?.Weekday);
        Assert.Equal(new TimeOnly(18, 30), parsed.TargetTime);
    }

    [Fact]
    public void Dative_day_without_a_time_is_the_target()
    {
        var parsed = IntentParser.Parse("Ece'nin dersini cumaya taşı");

        Assert.Null(parsed.SourceDay);
        Assert.Equal(DayOfWeek.Friday, parsed.TargetDay?.Weekday);
        Assert.Null(parsed.TargetTime);
    }

    [Fact]
    public void Relative_days_and_afternoon_hours_are_read()
    {
        var parsed = IntentParser.Parse("yarınki dersi saat 5'e çek");

        Assert.Equal(1, parsed.SourceDay?.OffsetDays);
        Assert.Equal(new TimeOnly(17, 0), parsed.TargetTime);
    }

    [Fact]
    public void Morning_keeps_an_early_hour_in_the_morning()
    {
        Assert.Equal(new TimeOnly(8, 30), IntentParser.Parse("dersi cumartesi sabah 8:30'a al").TargetTime);
        Assert.Equal(DayOfWeek.Saturday, IntentParser.Parse("dersi cumartesi sabah 8:30'a al").TargetDay?.Weekday);
    }

    [Fact]
    public void A_bare_number_is_not_a_time()
    {
        var parsed = IntentParser.Parse("Ece'nin 2 telafi hakkı var mı");

        Assert.Null(parsed.SourceTime);
        Assert.Null(parsed.TargetTime);
    }

    [Fact]
    public void Next_week_moves_the_weekday_one_week_ahead()
    {
        var parsed = IntentParser.Parse("Mennan'ın dersini haftaya salıya ertele");

        Assert.Equal(DayOfWeek.Tuesday, parsed.TargetDay?.Weekday);
        Assert.True(parsed.TargetDay?.NextWeek);
    }

    [Theory]
    [InlineData("Ece'nin dersini her hafta perşembe 15:00'e al", RescheduleScope.Recurring)]
    [InlineData("dersi bundan sonra pazartesi 18:00'e taşı", RescheduleScope.Recurring)]
    [InlineData("her cuma 17:00'ye alalım", RescheduleScope.Recurring)]
    [InlineData("bu seferlik cuma 17 de yapalım", RescheduleScope.Once)]
    [InlineData("sadece bu haftaki dersi 18:00'e al", RescheduleScope.Once)]
    [InlineData("perşembe dersini 17:00'ye al", RescheduleScope.Unspecified)]
    public void Detects_whether_the_change_is_one_off_or_recurring(string text, RescheduleScope expected)
    {
        Assert.Equal(expected, IntentParser.Parse(text).Scope);
    }

    [Fact]
    public void A_day_followed_by_a_time_is_the_target()
    {
        var parsed = IntentParser.Parse("dersi bundan sonra pazartesi 18:00'e taşı");

        Assert.Null(parsed.SourceDay);
        Assert.Equal(DayOfWeek.Monday, parsed.TargetDay?.Weekday);
        Assert.Equal(new TimeOnly(18, 0), parsed.TargetTime);
    }

    [Fact]
    public void Text_is_normalized_to_ascii_with_time_separators_kept()
    {
        Assert.Equal("mennan in dersini 17:30 a al", AssistantText.Normalize("Mennan'ın dersini 17.30'a al"));
        Assert.Equal("ilayda sen ogrenci cizelgesi", AssistantText.Normalize("İLAYDA ŞEN — Öğrenci Çizelgesi"));
    }

    [Theory]
    [InlineData(DayOfWeek.Thursday, false, "2026-10-29")]
    [InlineData(DayOfWeek.Monday, false, "2026-10-26")]
    [InlineData(DayOfWeek.Sunday, false, "2026-11-01")]
    [InlineData(DayOfWeek.Tuesday, true, "2026-11-03")]
    public void Weekday_resolves_to_the_next_occurrence_counting_today(DayOfWeek weekday, bool nextWeek, string expected)
    {
        var monday = new DateOnly(2026, 10, 26);

        Assert.Equal(DateOnly.Parse(expected), new DayReference(weekday, null, nextWeek).Resolve(monday));
    }

    [Fact]
    public void Relative_day_resolves_by_offset()
    {
        Assert.Equal(new DateOnly(2026, 10, 27), new DayReference(null, 1, false).Resolve(new DateOnly(2026, 10, 26)));
    }
}
