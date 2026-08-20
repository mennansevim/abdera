using System.Text.RegularExpressions;

namespace Abdera.Api.Modules.Banking.Domain;

// docs/12-bank-integration.md "Veli ↔ Receivable eşleştirme algoritması" - saf fonksiyon,
// veritabanına bağımlı değil (aday listesi ve kalan bakiyeler çağıran tarafından
// hesaplanıp verilir). Bilinçli olarak belirsizlikte otomatik davranmaz - para söz konusu
// olduğunda yanlış tahmin, sessiz yanlış eşleşmeden çok daha kötü.
public static class PaymentMatcher
{
    public record Candidate(Guid ReceivableId, string Period, decimal RemainingBalance);

    public static Guid? Match(IReadOnlyList<Candidate> candidates, decimal incomingAmount, string? description)
    {
        if (candidates.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(description))
        {
            var periodMatch = Regex.Match(description, @"\b20\d{2}-(0[1-9]|1[0-2])\b");
            if (periodMatch.Success)
            {
                var byPeriod = candidates.Where(c => c.Period == periodMatch.Value).ToList();
                if (byPeriod.Count == 1 && incomingAmount >= byPeriod[0].RemainingBalance)
                {
                    return byPeriod[0].ReceivableId;
                }
            }
        }

        var byAmount = candidates.Where(c => c.RemainingBalance == incomingAmount).ToList();
        return byAmount.Count == 1 ? byAmount[0].ReceivableId : null;
    }
}
