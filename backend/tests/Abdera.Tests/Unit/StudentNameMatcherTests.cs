using Abdera.Api.Modules.Assistant.Domain;

namespace Abdera.Tests.Unit;

public class StudentNameMatcherTests
{
    private static readonly StudentNameMatcher.Candidate MennanSevim = new(Guid.NewGuid(), "Mennan", "Sevim");
    private static readonly StudentNameMatcher.Candidate MennanKaya = new(Guid.NewGuid(), "Mennan", "Kaya");
    private static readonly StudentNameMatcher.Candidate EceDemir = new(Guid.NewGuid(), "Ece", "Demir");
    private static readonly StudentNameMatcher.Candidate AyseNur = new(Guid.NewGuid(), "Ayşe Nur", "Yıldız");
    private static readonly StudentNameMatcher.Candidate SuAk = new(Guid.NewGuid(), "Su", "Ak");
    private static readonly StudentNameMatcher.Candidate IlaydaSen = new(Guid.NewGuid(), "İlayda", "Şen");

    private static readonly StudentNameMatcher.Candidate[] All = [MennanSevim, MennanKaya, EceDemir, AyseNur, SuAk, IlaydaSen];

    private static StudentNameMatcher.Result Match(string text) =>
        StudentNameMatcher.Match(AssistantText.Tokens(AssistantText.Normalize(text)), All);

    [Fact]
    public void Full_name_with_a_suffix_matches_only_that_student()
    {
        Assert.Equal([MennanSevim.StudentId], Match("Mennan Sevim'in gelişimini getir").StudentIds);
    }

    [Fact]
    public void A_shared_first_name_returns_every_student_for_the_user_to_choose()
    {
        Assert.Equal([MennanSevim.StudentId, MennanKaya.StudentId], Match("Mennan'ın dersleri").StudentIds);
    }

    [Fact]
    public void A_full_name_match_wins_over_a_first_name_match()
    {
        Assert.Equal([MennanKaya.StudentId], Match("Mennan Kaya ve Mennan").StudentIds);
    }

    [Theory]
    [InlineData("Ecenin telafisi var mı")]
    [InlineData("ECE'YE TELAFİ")]
    [InlineData("ece ile ilgili")]
    public void Suffixes_and_case_do_not_break_the_match(string text)
    {
        Assert.Equal([EceDemir.StudentId], Match(text).StudentIds);
    }

    [Fact]
    public void Last_name_alone_matches_when_no_first_name_does()
    {
        Assert.Equal([MennanKaya.StudentId], Match("Kaya'nın aidatı").StudentIds);
    }

    [Fact]
    public void Two_word_first_names_and_turkish_capitals_match()
    {
        Assert.Equal([AyseNur.StudentId], Match("Ayşe Nur'un dersi").StudentIds);
        Assert.Equal([IlaydaSen.StudentId], Match("ILAYDA ŞEN'in gelişimi").StudentIds);
    }

    [Fact]
    public void Short_names_match_only_together_with_the_last_name()
    {
        Assert.Equal([SuAk.StudentId], Match("Su Ak'ın dersi").StudentIds);
        Assert.Empty(Match("bir bardak su").StudentIds);
    }

    [Fact]
    public void A_name_inside_another_word_does_not_match()
    {
        // "ecesi" ek değil; "Ece" ile eşleşmez.
        Assert.Empty(Match("pecesini getir").StudentIds);
        Assert.Empty(Match("ecesi").StudentIds);
    }

    [Fact]
    public void Matched_token_indexes_include_a_separately_written_suffix()
    {
        var tokens = AssistantText.Tokens(AssistantText.Normalize("Mennan Sevim'in gelişimi"));

        var result = StudentNameMatcher.Match(tokens, All);

        Assert.Equal(["mennan", "sevim", "in", "gelisimi"], tokens);
        Assert.Equal(new HashSet<int> { 0, 1, 2 }, result.MatchedTokenIndexes);
    }

    [Fact]
    public void Full_name_indexes_find_people_named_with_first_and_last_name_only()
    {
        var teacher = new StudentNameMatcher.Candidate(Guid.NewGuid(), "Onur", "Koç");
        var tokens = AssistantText.Tokens(AssistantText.Normalize("Onur Koç'un ve Koç'un dersleri"));

        // "onur koc un ve koc un dersleri": ad + soyad (0, 1) ve ayrı yazılmış eki (2). Tek
        // başına "Koç" (4) öğretmen sayılmaz.
        Assert.Equal(new HashSet<int> { 0, 1, 2 }, StudentNameMatcher.FullNameIndexes(tokens, [teacher]));
    }
}
