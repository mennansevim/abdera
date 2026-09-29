using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

// Aynı örnekler NormalizeStudentNames migration'ının SQL'iyle postgres:16-alpine üzerinde
// elle doğrulandı - iki eşleme birbirinden ayrışmamalı.
public class PersonNameFormatterTests
{
    [Theory]
    [InlineData("miray sevim", "Miray Sevim")]
    [InlineData("MİRAY SEVİM", "Miray Sevim")]
    [InlineData("ışıl", "Işıl")]
    [InlineData("IŞIL", "Işıl")]
    [InlineData("ilayda", "İlayda")]
    [InlineData("  ayşe   nur  ", "Ayşe Nur")]
    [InlineData("ayşe-nur", "Ayşe-Nur")]
    [InlineData("ÇAĞLAR ÖZGÜR", "Çağlar Özgür")]
    [InlineData("Miray", "Miray")]
    public void Format_title_cases_each_word_with_turkish_i_rules(string input, string expected)
    {
        Assert.Equal(expected, PersonNameFormatter.Format(input));
    }
}
