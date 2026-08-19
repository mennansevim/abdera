using System.Text.RegularExpressions;

namespace Abdera.Api.Shared;

// docs/00-master-prompt.md People: "Normalize phone numbers to international form where
// possible." Okulun ilk müşterisi Türkiye'de olduğu için Türk numara biçimlerini E.164'e
// (+90XXXXXXXXXX) çevirir. Tam bir libphonenumber entegrasyonu bu ölçek için fazladır
// (CLAUDE.md - over-engineering'den kaçın); yalnızca gerçekte karşılaşılan biçimleri kapsar.
public static partial class PhoneNumberNormalizer
{
    [GeneratedRegex(@"[^\d+]")]
    private static partial Regex NonDigitOrPlus();

    public static string Normalize(string rawInput)
    {
        var cleaned = NonDigitOrPlus().Replace(rawInput.Trim(), "");

        var normalized = cleaned switch
        {
            // +905551234567 - zaten E.164
            _ when cleaned.StartsWith("+90") && cleaned.Length == 13 => cleaned,
            // 905551234567
            _ when cleaned.StartsWith("90") && cleaned.Length == 12 => "+" + cleaned,
            // 05551234567 (yerel, başında sıfır)
            _ when cleaned.StartsWith("0") && cleaned.Length == 11 => "+90" + cleaned[1..],
            // 5551234567 (alan kodsuz, sıfırsız)
            _ when cleaned.Length == 10 && cleaned.StartsWith("5") => "+90" + cleaned,
            _ => throw new ArgumentException(
                $"'{rawInput}' geçerli bir Türkiye telefon numarasına benzemiyor. Örnek: 0555 123 45 67."),
        };

        return normalized;
    }
}
