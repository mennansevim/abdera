using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

// docs/10-decisions.md Q1: veli varsayılan şifresi = soyad + adın ilk harfi, küçük harf, ASCII.
public class GuardianPasswordGeneratorTests
{
    [Theory]
    [InlineData("Mennan", "Sevim", "sevimm")]
    [InlineData("Ayşe", "Çelik", "celika")]
    [InlineData("İsmail", "Güneş", "gunesi")]
    [InlineData("Ayşe Nur", "Öztürk Şahin", "ozturksahina")]
    [InlineData("  Ali ", " Kaya-Demir ", "kayademira")]
    public void Derives_surname_plus_first_initial(string firstName, string lastName, string expected)
    {
        Assert.Equal(expected, GuardianPasswordGenerator.Generate(firstName, lastName));
    }
}
