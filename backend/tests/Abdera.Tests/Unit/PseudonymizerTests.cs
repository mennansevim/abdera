using Abdera.Api.Modules.Assistant.Domain;
using Abdera.Api.Modules.Assistant.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Abdera.Tests.Unit;

// Dil modeline kişi adı gitmez (docs/10-decisions.md AE9).
public class PseudonymizerTests
{
    private static readonly Pseudonymizer.Person Deniz = new(Guid.NewGuid(), "Deniz", "Arslan", false);
    private static readonly Pseudonymizer.Person Lara = new(Guid.NewGuid(), "Lara", "Arslan", false);
    private static readonly Pseudonymizer.Person Ada = new(Guid.NewGuid(), "Ada", "Çetin", false);
    private static readonly Pseudonymizer.Person Ilayda = new(Guid.NewGuid(), "İlayda", "Öztürk", false);
    private static readonly Pseudonymizer.Person Mina = new(Guid.NewGuid(), "Mina", "Şahin", false);
    private static readonly Pseudonymizer.Person Ece = new(Guid.NewGuid(), "Ece", "Şahin", true);

    private static readonly Pseudonymizer Names = new([Deniz, Lara, Ada, Ilayda, Mina, Ece]);

    private static string Code(Pseudonymizer.Person person) => Names.CodeFor(person.Id);

    [Fact]
    public void Full_names_become_codes_and_keep_their_suffix()
    {
        var masked = Names.Mask("Deniz Arslan'ın gelişimini getir, Ada Çetin'e telafi aç");

        Assert.Equal($"{Code(Deniz)}'ın gelişimini getir, {Code(Ada)}'e telafi aç", masked);
        Assert.DoesNotContain("Deniz", masked);
        Assert.DoesNotContain("Çetin", masked);
    }

    [Fact]
    public void A_first_name_used_by_one_student_only_is_masked_with_or_without_apostrophe()
    {
        Assert.Equal($"{Code(Deniz)}'in dersleri", Names.Mask("Deniz'in dersleri"));
        Assert.Equal($"{Code(Deniz)}'in dersleri", Names.Mask("DENİZİN dersleri"));
        Assert.Equal($"{Code(Ilayda)} nasıl", Names.Mask("ilayda nasıl"));
    }

    [Fact]
    public void A_shared_surname_alone_stays_as_written_for_the_model_to_search()
    {
        // İki Arslan kardeş: tek başına "Arslan" bir kişiyi belirtmez.
        Assert.Equal("Arslan'ın dersleri", Names.Mask("Arslan'ın dersleri"));
        // "Şahin" hem öğrenci Mina'nın hem öğretmen Ece'nin soyadı.
        Assert.Equal("Şahin'in dersleri", Names.Mask("Şahin'in dersleri"));
    }

    [Fact]
    public void Teachers_are_masked_with_their_own_prefix()
    {
        var masked = Names.Mask("Ece Şahin'in öğrencileri");

        Assert.StartsWith("T-", Code(Ece));
        Assert.Equal($"{Code(Ece)}'in öğrencileri", masked);
    }

    [Fact]
    public void A_name_inside_another_word_is_not_masked()
    {
        Assert.Equal("Adanalı bir veli aradı", Names.Mask("Adanalı bir veli aradı"));
    }

    [Fact]
    public void Codes_in_the_answer_become_names_with_the_right_vowel_harmony()
    {
        var answer = $"{Code(Deniz)}'nin devamı iyi. {Code(Lara)}'ye kart açtım. {Code(Ada)} için not yok. {Code(Ece)}'den bilgi al.";

        Assert.Equal("Deniz Arslan'ın devamı iyi. Lara Arslan'a kart açtım. Ada Çetin için not yok. Ece Şahin'den bilgi al.", Names.Unmask(answer));
    }

    [Fact]
    public void Codes_are_stable_for_the_same_person_and_unknown_codes_are_left_alone()
    {
        var again = new Pseudonymizer([Ada, Deniz]);

        Assert.Equal(Code(Deniz), again.CodeFor(Deniz.Id));
        Assert.True(Names.TryResolve(Code(Mina), out var id));
        Assert.Equal(Mina.Id, id);
        Assert.Equal("Ö-ZZZZ yok", Names.Unmask("Ö-ZZZZ yok"));
    }

    [Theory]
    [InlineData("Arslan", "nin", "ın")]
    [InlineData("Çetin", "ye", "e")]
    [InlineData("Kurt", "da", "ta")]
    [InlineData("Koç", "den", "tan")]
    [InlineData("Öztürk", "a", "e")]
    [InlineData("Aydın", "u", "ı")]
    [InlineData("Eren", "la", "le")]
    public void Turkish_suffixes_follow_the_name(string name, string suffix, string expected)
    {
        Assert.Equal(expected, TurkishSuffix.Harmonize(name, suffix));
    }

    [Fact]
    public void Chatbot_options_read_the_flat_env_names_and_default_to_deepseek()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai_Chatbot_ApiKey"] = "test-key",
        }).Build();

        var options = ChatbotOptions.FromConfiguration(configuration);

        Assert.True(options.IsEnabled);
        Assert.Equal("https://api.deepseek.com", options.BaseUrl);
        Assert.Equal("deepseek-flash", options.Model);
        Assert.True(options.IsDeepSeek);
        Assert.False(ChatbotOptions.FromConfiguration(new ConfigurationBuilder().Build()).IsEnabled);
    }
}
