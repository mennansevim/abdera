using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("0555 123 45 67", "+905551234567")]
    [InlineData("05551234567", "+905551234567")]
    [InlineData("5551234567", "+905551234567")]
    [InlineData("+90 555 123 45 67", "+905551234567")]
    [InlineData("905551234567", "+905551234567")]
    [InlineData("+905551234567", "+905551234567")]
    public void Normalize_converts_common_turkish_formats_to_e164(string input, string expected)
    {
        Assert.Equal(expected, PhoneNumberNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("not a phone number")]
    [InlineData("+1 555 123 4567")] // desteklenmeyen ülke kodu
    public void Normalize_throws_for_invalid_input(string input)
    {
        Assert.Throws<ArgumentException>(() => PhoneNumberNormalizer.Normalize(input));
    }
}
