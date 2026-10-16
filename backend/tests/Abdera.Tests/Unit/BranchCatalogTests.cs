using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared.Branches;

namespace Abdera.Tests.Unit;

public class BranchCatalogTests
{
    [Fact]
    public void Single_category_uses_its_own_term_and_mixed_or_empty_uses_branch()
    {
        Assert.Equal(("Enstrüman", "Enstrümanlar"), BranchCatalog.TermsFor(["music", "music"]));
        Assert.Equal(("Dans türü", "Dans türleri"), BranchCatalog.TermsFor(["dance"]));
        Assert.Equal(("Branş", "Branşlar"), BranchCatalog.TermsFor(["music", "dance"]));
        Assert.Equal(("Branş", "Branşlar"), BranchCatalog.TermsFor([]));
    }

    [Theory]
    [InlineData("İngilizce - Konuşma", "INGILIZCE_KONUSMA")]
    [InlineData("Robotik kodlama", "ROBOTIK_KODLAMA")]
    [InlineData("Çocuk dansı", "COCUK_DANSI")]
    [InlineData("???", "DERS")]
    public void Code_is_derived_from_the_name(string name, string code) => Assert.Equal(code, BranchCatalog.CodeFor(name));

    [Fact]
    public void Catalog_keys_are_unique_and_unknown_category_is_rejected()
    {
        Assert.Equal(BranchCatalog.All.Count, BranchCatalog.All.Select(c => c.Key).Distinct().Count());
        Assert.Throws<ArgumentException>(() => Instrument.Create("Uçan halı", "UCAN", "magic"));
        Assert.Equal("dance", Instrument.Create("Bale", "BALE", "DANCE").Category);
    }

    [Theory]
    [InlineData("Yoga ve meditasyon", "c-yoga-ve-meditasyon")]
    [InlineData("Çocuk atölyesi", "c-cocuk-atolyesi")]
    public void Custom_key_is_derived_and_recognised(string name, string key)
    {
        Assert.Equal(key, BranchCatalog.CustomKeyFor(name));
        Assert.True(BranchCatalog.IsCustomKey(key));
        Assert.False(BranchCatalog.IsCustomKey("music"));
        Assert.False(BranchCatalog.IsCustomKey("c-"));
    }

    [Theory]
    [InlineData("Ders", "Dersler")]
    [InlineData("Seans", "Seanslar")]
    [InlineData("Atölye", "Atölyeler")]
    public void Plural_follows_vowel_harmony(string singular, string plural) => Assert.Equal(plural, BranchCatalog.PluralOf(singular));

    [Fact]
    public void Terms_use_a_custom_category_label_when_it_is_the_only_one()
    {
        var yoga = new BranchCategory("c-yoga", "Yoga", "Seans", "Seanslar", []);
        Assert.Equal(("Seans", "Seanslar"), BranchCatalog.TermsFor(["c-yoga"], [.. BranchCatalog.All, yoga]));
    }
}
