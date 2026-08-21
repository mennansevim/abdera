using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Banking.Domain;
using Abdera.Api.Modules.Banking.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Pricing.Domain;
using Abdera.Api.Modules.Pricing.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// docs/12-bank-integration.md uçtan uca akışlar. docs/10-decisions.md E1: bilinçli olarak
// belirsizlikte otomatik davranmaz - bu dosyanın en önemli testi ambiguous senaryonun
// gerçekten NeedsReview'da kaldığını doğrulayan testtir.
public class BankingFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public BankingFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private record SeededReceivable(Guid GuardianId, Guid ReceivableId, decimal Amount, string Period);

    private static int _nextDurationMinutes = 30;

    // Enstrüman -> öğretmen -> öğrenci -> veli -> kayıt -> fiyat listesi -> fee plan ->
    // Receivable zincirini kurar; diğer entegrasyon test dosyalarındaki SeedLessonAsync
    // ile aynı desende ama Billing tarafına odaklı. Her çağrı kendine özgü bir
    // durationMinutes kullanır ki price_list_items'ın enstrüman+süre+tip çakışma kontrolü
    // (docs/10-decisions.md, price_list_items çakışma kısıtı) aynı test sınıfındaki
    // birden fazla SeedReceivableAsync çağrısını birbirine çarptırmasın.
    private static async Task<SeededReceivable> SeedReceivableAsync(HttpClient admin, string suffix, decimal amount = 1000m, string period = "2026-09")
    {
        var durationMinutes = Interlocked.Increment(ref _nextDurationMinutes);

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest($"BTeacher{suffix}", "Soyad", [piano.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest($"BStudent{suffix}", "Soyad", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        var phoneDigits = (Math.Abs(suffix.GetHashCode()) % 10_000_000).ToString("D7");
        var guardian = (await (await admin.PostAsJsonAsync("/api/guardians",
                new Guardians.CreateRequest($"BGuardian{suffix}", "Soyad", $"0555{phoneDigits}")))
            .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;
        await admin.PostAsJsonAsync($"/api/students/{student.Id}/guardians",
            new LinkGuardianToStudent.Request(guardian.Id, "anne", true));

        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var priceListHttpResponse = await admin.PostAsJsonAsync("/api/price-lists", new PriceLists.CreateRequest(
            $"Banking Test {suffix}", new DateOnly(2026, 1, 1), null,
            [new PriceLists.CreateItemRequest(piano.Id, durationMinutes, BillingType.Monthly, amount, "TRY", null)]));
        var priceListRawBody = await priceListHttpResponse.Content.ReadAsStringAsync();
        if (!priceListHttpResponse.IsSuccessStatusCode)
            throw new Exception($"price-list create failed: {priceListHttpResponse.StatusCode} - {priceListRawBody}");
        var priceList = System.Text.Json.JsonSerializer.Deserialize<PriceLists.PriceListResponse>(priceListRawBody, TestJson.Options)!;
        var priceListItem = priceList.Items.Single();

        await admin.PostAsJsonAsync($"/api/enrollments/{enrollment.Id}/fee-plan",
            new FeePlans.CreateRequest(priceListItem.Id, DueDay: 5, new DateOnly(2026, 8, 1)));

        var receivableResponse = await admin.PostAsJsonAsync("/api/receivables",
            new Receivables.CreateRequest(enrollment.Id, period));
        var receivable = (await receivableResponse.Content.ReadFromJsonAsync<Receivables.ReceivableResponse>(TestJson.Options))!;

        return new SeededReceivable(guardian.Id, receivable.Id, amount, period);
    }

    private static async Task<string> AssignVirtualIbanAsync(HttpClient admin, Guid guardianId)
    {
        var response = await admin.PostAsync($"/api/guardians/{guardianId}/virtual-iban", null);
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<AssignVirtualIban.VirtualIbanResponse>(TestJson.Options))!;
        return body.Iban;
    }

    [Fact]
    public async Task Assigning_a_second_active_virtual_iban_to_the_same_guardian_is_rejected()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedReceivableAsync(admin, "viban1");

        await AssignVirtualIbanAsync(admin, seeded.GuardianId);
        var second = await admin.PostAsync($"/api/guardians/{seeded.GuardianId}/virtual-iban", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Incoming_transaction_with_exact_amount_match_auto_creates_payment_and_updates_receivable()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedReceivableAsync(admin, "match1", amount: 1500m, period: "2026-09");
        var iban = await AssignVirtualIbanAsync(admin, seeded.GuardianId);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/bank/simulate-transaction", new
        {
            virtualIban = iban,
            amount = 1500m,
            senderName = "Ayşe Yılmaz",
            description = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var receivable = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == seeded.ReceivableId);
        Assert.Equal(ReceivableStatus.Paid, receivable.Status);

        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.ReceivableId == seeded.ReceivableId);
        Assert.Equal(1500m, payment.Amount);
        Assert.Null(payment.CreatedBy); // otomatik eşleşen ödemede admin yok (docs/10-decisions.md E1)

        var transaction = await db.BankIncomingTransactions.AsNoTracking().SingleAsync(t => t.MatchedReceivableId == seeded.ReceivableId);
        Assert.Equal(BankIncomingTransactionStatus.Matched, transaction.Status);
    }

    [Fact]
    public async Task Incoming_transaction_with_ambiguous_amount_stays_needs_review_and_does_not_touch_receivable()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        // Aynı veliye bağlı iki farklı öğrenci/kayıt, aynı tutarlı iki açık Receivable -
        // otomatik eşleştirme belirsiz kalmalı.
        var first = await SeedReceivableAsync(admin, "ambig1", amount: 1200m, period: "2026-09");
        var guardianClient = admin;
        var secondStudent = (await (await guardianClient.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("AmbigSecond", "Soyad", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        await guardianClient.PostAsJsonAsync($"/api/students/{secondStudent.Id}/guardians",
            new LinkGuardianToStudent.Request(first.GuardianId, "anne", false));

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var guitar = instruments!.Single(i => i.Code == "GUITAR");
        var teacher2 = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("AmbigTeacher", "Soyad", [guitar.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var enrollment2 = (await (await admin.PostAsJsonAsync($"/api/students/{secondStudent.Id}/enrollments",
                new Enrollments.CreateRequest(teacher2.Id, guitar.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var priceList = (await (await admin.PostAsJsonAsync("/api/price-lists", new PriceLists.CreateRequest(
                "Ambig Second List", new DateOnly(2026, 1, 1), null,
                [new PriceLists.CreateItemRequest(guitar.Id, 45, BillingType.Monthly, 1200m, "TRY", null)])))
            .Content.ReadFromJsonAsync<PriceLists.PriceListResponse>(TestJson.Options))!;
        await admin.PostAsJsonAsync($"/api/enrollments/{enrollment2.Id}/fee-plan",
            new FeePlans.CreateRequest(priceList.Items.Single().Id, DueDay: 5, new DateOnly(2026, 8, 1)));
        var secondReceivableResponse = await admin.PostAsJsonAsync("/api/receivables",
            new Receivables.CreateRequest(enrollment2.Id, "2026-09"));
        var secondReceivable = (await secondReceivableResponse.Content.ReadFromJsonAsync<Receivables.ReceivableResponse>(TestJson.Options))!;

        var iban = await AssignVirtualIbanAsync(admin, first.GuardianId);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/bank/simulate-transaction", new
        {
            virtualIban = iban,
            amount = 1200m,
            senderName = "Belirsiz Veli",
            description = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var firstReceivableAfter = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == first.ReceivableId);
        var secondReceivableAfter = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == secondReceivable.Id);
        Assert.Equal(ReceivableStatus.Unpaid, firstReceivableAfter.Status);
        Assert.Equal(ReceivableStatus.Unpaid, secondReceivableAfter.Status);

        var virtualIbanId = await db.VirtualIbans.Where(v => v.Iban == iban).Select(v => v.Id).SingleAsync();
        var transaction = await db.BankIncomingTransactions.AsNoTracking().SingleAsync(t => t.VirtualIbanId == virtualIbanId);
        Assert.Equal(BankIncomingTransactionStatus.NeedsReview, transaction.Status);
        Assert.Null(transaction.MatchedReceivableId);
    }

    [Fact]
    public async Task Admin_can_manually_resolve_a_needs_review_transaction()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedReceivableAsync(admin, "resolve1", amount: 800m, period: "2026-09");
        var iban = await AssignVirtualIbanAsync(admin, seeded.GuardianId);

        var devClient = _factory.CreateClient();
        // Açıklamasız + tutarı kasıtlı farklı (kalan bakiyeden az) gönderip NeedsReview'a düşürüyoruz.
        await devClient.PostAsJsonAsync("/api/dev/bank/simulate-transaction", new
        {
            virtualIban = iban,
            amount = 300m,
            senderName = "Kısmi Gönderim",
            description = (string?)null,
        });

        // ARC-3: liste artık { items, totalCount, page, pageSize } zarfı dönüyor.
        var pending = await admin.GetFromJsonAsync<PagedResponse<BankTransactions.TransactionResponse>>(
            "/api/bank-transactions?status=NeedsReview", TestJson.Options);
        var transaction = pending!.Items.Single(t => t.GuardianId == seeded.GuardianId);

        var resolveResponse = await admin.PostAsJsonAsync($"/api/bank-transactions/{transaction.Id}/resolve",
            new BankTransactions.ResolveRequest(seeded.ReceivableId));
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var receivable = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == seeded.ReceivableId);
        Assert.Equal(ReceivableStatus.Partial, receivable.Status);

        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.ReceivableId == seeded.ReceivableId);
        Assert.NotNull(payment.CreatedBy); // elle çözüldüğü için bu sefer bir admin var
    }

    [Fact]
    public async Task Duplicate_provider_transaction_id_is_not_processed_twice()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedReceivableAsync(admin, "idem1", amount: 500m, period: "2026-09");
        var iban = await AssignVirtualIbanAsync(admin, seeded.GuardianId);
        var providerTransactionId = $"fixed-{Guid.NewGuid()}";

        var client = _factory.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            await client.PostAsJsonAsync("/api/dev/bank/simulate-transaction", new
            {
                virtualIban = iban,
                amount = 500m,
                senderName = "Tekrar Gönderim",
                description = (string?)null,
                providerTransactionId,
            });
        }

        var transactionCount = await db.BankIncomingTransactions.CountAsync(t => t.ProviderTransactionId == providerTransactionId);
        Assert.Equal(1, transactionCount);

        var paymentCount = await db.Payments.CountAsync(p => p.ReceivableId == seeded.ReceivableId);
        Assert.Equal(1, paymentCount);
    }
}
