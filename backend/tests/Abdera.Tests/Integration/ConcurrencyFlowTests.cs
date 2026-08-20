using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Pricing.Domain;
using Abdera.Api.Modules.Pricing.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// ARC-1 (docs/13-audit-fix-prompt.md): CLAUDE.md "Eşzamanlı düzenleme riski olan tablolarda
// optimistic concurrency (xmin veya rowversion kolonu)" kuralı. Bu dosya, xmin tabanlı
// concurrency token'ın (bkz. ReceivableConfiguration.cs, docs/08-migrations.md
// "Optimistic concurrency (xmin)") gerçek bir Postgres'e karşı GERÇEKTEN çalıştığını
// doğrular - iki admin aynı Receivable'a aynı anda ödeme işlerse ikinci yazma birincisini
// sessizce EZMEMELİ.
public class ConcurrencyFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public ConcurrencyFlowTests(AbderaWebApplicationFactory factory)
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

    private static async Task<Guid> SeedReceivableAsync(HttpClient admin, string suffix)
    {
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        // price_list_items'daki "aynı enstrüman x süre için çakışan yürürlük tarihi aralığı
        // olamaz" kuralına takılmamak için süreyi tekilleştiriyoruz (bkz. BankingFlowTests
        // yorumundaki aynı desen).
        var durationMinutes = 30 + (Math.Abs(suffix.GetHashCode()) % 90);
        var priceListResponse = await admin.PostAsJsonAsync("/api/price-lists", new PriceLists.CreateRequest(
            $"Concurrency Testi {suffix}", new DateOnly(2026, 1, 1), null,
            [new PriceLists.CreateItemRequest(piano.Id, durationMinutes, BillingType.Monthly, 1000m, "TRY", null)]));
        var priceList = (await priceListResponse.Content.ReadFromJsonAsync<PriceLists.PriceListResponse>(TestJson.Options))!;
        var item = priceList.Items.Single();

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest($"Concurrency{suffix}", "Teacher", [piano.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest($"Concurrency{suffix}", "Student", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, piano.Id, new DateOnly(2026, 1, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        await admin.PostAsJsonAsync($"/api/enrollments/{enrollment.Id}/fee-plan",
            new FeePlans.CreateRequest(item.Id, DueDay: 5, new DateOnly(2026, 1, 1)));

        var receivableResponse = await admin.PostAsJsonAsync("/api/receivables",
            new Receivables.CreateRequest(enrollment.Id, "2026-09"));
        receivableResponse.EnsureSuccessStatusCode();
        var receivable = (await receivableResponse.Content.ReadFromJsonAsync<Receivables.ReceivableResponse>(TestJson.Options))!;
        return receivable.Id;
    }

    [Fact]
    public async Task Second_concurrent_SaveChanges_on_same_receivable_throws_concurrency_exception()
    {
        var admin = await CreateAdminClientAsync();
        var receivableId = await SeedReceivableAsync(admin, "09concurrency");

        // Aynı satırı iki AYRI DbContext ile oku - her ikisi de aynı xmin değerini görür,
        // tıpkı iki ayrı admin isteğinin scoped DbContext'leri gibi.
        await using var context1 = await _factory.CreateDbContextAsync();
        await using var context2 = await _factory.CreateDbContextAsync();

        var receivable1 = await context1.Receivables.SingleAsync(r => r.Id == receivableId);
        var receivable2 = await context2.Receivables.SingleAsync(r => r.Id == receivableId);

        receivable1.RecordPaymentEffect(1000m, DateTimeOffset.UtcNow); // -> Paid
        receivable2.RecordPaymentEffect(500m, DateTimeOffset.UtcNow); // -> Partial

        // İlk yazma başarılı - xmin ilerler.
        await context1.SaveChangesAsync();

        // İkinci yazma artık eski (stale) xmin ile geliyor - sessizce ezmek yerine
        // DbUpdateConcurrencyException fırlatmalı (GlobalExceptionHandler bunu HTTP
        // seviyesinde 409'a çevirir).
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context2.SaveChangesAsync());

        // Kazanan ilk yazma - kayıt sessizce ezilmedi, Paid olarak kaldı.
        await using var verifyContext = await _factory.CreateDbContextAsync();
        var final = await verifyContext.Receivables.AsNoTracking().SingleAsync(r => r.Id == receivableId);
        Assert.Equal(ReceivableStatus.Paid, final.Status);
    }

    [Fact]
    public async Task Second_concurrent_SaveChanges_on_same_bank_transaction_throws_concurrency_exception()
    {
        // ARC-1 kapsamındaki ikinci tablo: bank_incoming_transactions (Match/Ignore/Resolve
        // işlemleri aynı satıra eşzamanlı uygulanabilir, bkz. Modules/Banking/Features/Webhooks.cs).
        var admin = await CreateAdminClientAsync();
        var guardian = (await (await admin.PostAsJsonAsync("/api/guardians",
                new Guardians.CreateRequest("ConcurrencyVeli", "Soyad", "05551112233")))
            .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;
        var ibanResponse = await admin.PostAsJsonAsync($"/api/guardians/{guardian.Id}/virtual-iban", new { });
        ibanResponse.EnsureSuccessStatusCode();

        await using var db = await _factory.CreateDbContextAsync();
        var virtualIban = await db.VirtualIbans.SingleAsync(v => v.GuardianId == guardian.Id);

        var simulateResponse = await admin.PostAsJsonAsync("/api/dev/bank/simulate-transaction", new
        {
            providerTransactionId = $"concurrency-{Guid.NewGuid():N}",
            virtualIban = virtualIban.Iban,
            amount = 1234.56m,
            currency = "TRY",
            senderName = "Concurrency Test",
            description = (string?)null,
            receivedAt = DateTimeOffset.UtcNow,
        });
        simulateResponse.EnsureSuccessStatusCode();

        var transactionId = await db.BankIncomingTransactions.AsNoTracking()
            .Where(t => t.VirtualIbanId == virtualIban.Id)
            .Select(t => t.Id)
            .SingleAsync();

        await using var context1 = await _factory.CreateDbContextAsync();
        await using var context2 = await _factory.CreateDbContextAsync();

        var transaction1 = await context1.BankIncomingTransactions.SingleAsync(t => t.Id == transactionId);
        var transaction2 = await context2.BankIncomingTransactions.SingleAsync(t => t.Id == transactionId);

        transaction1.Ignore(DateTimeOffset.UtcNow);
        transaction2.Ignore(DateTimeOffset.UtcNow);

        await context1.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context2.SaveChangesAsync());
    }
}
