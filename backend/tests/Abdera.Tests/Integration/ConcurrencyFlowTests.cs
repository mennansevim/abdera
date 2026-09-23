using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
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

    // Tarife ve indirim politikası migration ile seed edildiği için (bkz.
    // TuitionAndDuesFlowTests) aidat açmak yalnızca kurs kaydı gerektiriyor - eski
    // "fiyat listesi + ücret planı" zinciri ve onun çakışma kontrolünü aşmak için
    // uydurulan tekil durationMinutes hilesi artık gereksiz.
    private async Task<Guid> SeedReceivableAsync(HttpClient admin, string suffix)
    {
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest($"Concurrency{suffix}", "Teacher", [piano.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest($"Concurrency{suffix}", "Student", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, piano.Id, new DateOnly(2026, 9, 1), CourseKind.Individual)))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        // Kayıt açılışı bu ayın aidatını zaten açtı (EnrollmentReceivableOpener) - ayrıca
        // POST /api/receivables ile açmaya çalışmak 409 verirdi. O satırı kullan.
        var period = TestPeriods.Current(_factory.Services);
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Receivables.AsNoTracking()
            .Where(r => r.EnrollmentId == enrollment.Id && r.Period == period)
            .Select(r => r.Id)
            .SingleAsync();
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

        receivable1.RecordPaymentEffect(6000m, DateTimeOffset.UtcNow); // -> Paid (Birebir tarifesi)
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
