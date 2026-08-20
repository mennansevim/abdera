using Abdera.Api.Modules.Banking.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

public class BankingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void VirtualIban_Deactivate_throws_when_already_inactive()
    {
        var iban = VirtualIban.Create(Guid.NewGuid(), "TR330006100519786457841326", "Fake", "ref-1", Now);
        iban.Deactivate(Now);

        Assert.Throws<ConflictException>(() => iban.Deactivate(Now));
    }

    private static BankIncomingTransaction CreateTransaction(decimal amount = 1000m) =>
        BankIncomingTransaction.Receive(Guid.NewGuid(), "Fake", $"tx-{Guid.NewGuid()}", amount, "TRY", "Ayşe Yılmaz", "2026-09", Now, Now);

    [Fact]
    public void BankIncomingTransaction_RecordMatch_sets_matched_receivable_and_status()
    {
        var transaction = CreateTransaction();
        var receivableId = Guid.NewGuid();

        transaction.RecordMatch(receivableId, Now);

        Assert.Equal(BankIncomingTransactionStatus.Matched, transaction.Status);
        Assert.Equal(receivableId, transaction.MatchedReceivableId);
    }

    [Fact]
    public void BankIncomingTransaction_cannot_be_matched_twice()
    {
        var transaction = CreateTransaction();
        transaction.RecordMatch(Guid.NewGuid(), Now);

        Assert.Throws<ConflictException>(() => transaction.RecordMatch(Guid.NewGuid(), Now));
    }

    [Fact]
    public void BankIncomingTransaction_MarkNeedsReview_then_RecordMatch_succeeds()
    {
        // docs/12-bank-integration.md: NeedsReview'a düşen bir işlem admin tarafından
        // elle bir Receivable'a bağlanabilmeli.
        var transaction = CreateTransaction();
        transaction.MarkNeedsReview(Now);

        transaction.RecordMatch(Guid.NewGuid(), Now);

        Assert.Equal(BankIncomingTransactionStatus.Matched, transaction.Status);
    }

    [Fact]
    public void BankIncomingTransaction_Ignore_throws_when_already_matched()
    {
        var transaction = CreateTransaction();
        transaction.RecordMatch(Guid.NewGuid(), Now);

        Assert.Throws<ConflictException>(() => transaction.Ignore(Now));
    }

    [Fact]
    public void BankIncomingTransaction_Ignore_succeeds_from_needs_review()
    {
        var transaction = CreateTransaction();
        transaction.MarkNeedsReview(Now);

        transaction.Ignore(Now);

        Assert.Equal(BankIncomingTransactionStatus.Ignored, transaction.Status);
    }

    // --- PaymentMatcher ---

    [Fact]
    public void PaymentMatcher_returns_null_when_no_candidates()
    {
        var result = PaymentMatcher.Match([], 1000m, null);

        Assert.Null(result);
    }

    [Fact]
    public void PaymentMatcher_matches_single_candidate_with_exact_remaining_balance()
    {
        var receivableId = Guid.NewGuid();
        var candidates = new[] { new PaymentMatcher.Candidate(receivableId, "2026-09", 1000m) };

        var result = PaymentMatcher.Match(candidates, 1000m, null);

        Assert.Equal(receivableId, result);
    }

    [Fact]
    public void PaymentMatcher_does_not_match_when_amount_matches_more_than_one_candidate()
    {
        // İki farklı öğrencinin aynı tutarlı aidatı - isim/tutar tesadüfen çakışabilir,
        // otomatik tahmin etmek yerine belirsiz bırakılmalı (docs/12-bank-integration.md).
        var candidates = new[]
        {
            new PaymentMatcher.Candidate(Guid.NewGuid(), "2026-09", 1000m),
            new PaymentMatcher.Candidate(Guid.NewGuid(), "2026-09", 1000m),
        };

        var result = PaymentMatcher.Match(candidates, 1000m, null);

        Assert.Null(result);
    }

    [Fact]
    public void PaymentMatcher_does_not_match_when_amount_matches_no_candidate()
    {
        var candidates = new[] { new PaymentMatcher.Candidate(Guid.NewGuid(), "2026-09", 1000m) };

        var result = PaymentMatcher.Match(candidates, 750m, null);

        Assert.Null(result);
    }

    [Fact]
    public void PaymentMatcher_prefers_description_period_match_over_ambiguous_amount()
    {
        // Aynı tutarlı iki aday var (amount-only eşleşme belirsiz olurdu), ama açıklamada
        // dönem bilgisi ("2026-10") tek bir adayı işaret ediyor - bu tercih edilir.
        var septemberId = Guid.NewGuid();
        var octoberId = Guid.NewGuid();
        var candidates = new[]
        {
            new PaymentMatcher.Candidate(septemberId, "2026-09", 1000m),
            new PaymentMatcher.Candidate(octoberId, "2026-10", 1000m),
        };

        var result = PaymentMatcher.Match(candidates, 1000m, "Ekim aidatı 2026-10 için");

        Assert.Equal(octoberId, result);
    }

    [Fact]
    public void PaymentMatcher_description_match_requires_amount_to_cover_remaining_balance()
    {
        var receivableId = Guid.NewGuid();
        var candidates = new[] { new PaymentMatcher.Candidate(receivableId, "2026-09", 1000m) };

        // Açıklamada dönem doğru ama gönderilen tutar kalan bakiyeden az - eşleşmemeli.
        var result = PaymentMatcher.Match(candidates, 500m, "2026-09 aidatı");

        Assert.Null(result);
    }

    [Fact]
    public void PaymentMatcher_falls_back_to_amount_match_when_description_period_not_found_among_candidates()
    {
        var receivableId = Guid.NewGuid();
        var candidates = new[] { new PaymentMatcher.Candidate(receivableId, "2026-09", 1000m) };

        // Açıklamadaki dönem ("2026-11") adaylar arasında yok - amount-only eşleşmeye düşer.
        var result = PaymentMatcher.Match(candidates, 1000m, "2026-11 için gönderim");

        Assert.Equal(receivableId, result);
    }
}
