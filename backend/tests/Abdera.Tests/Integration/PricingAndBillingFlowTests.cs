using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Pricing.Domain;
using Abdera.Api.Modules.Pricing.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// docs/00-master-prompt.md kabul kriterleri: "an administrator can record cash, transfer,
// card, or other payments... receivable statuses are correct." docs/10-decisions.md A1:
// fiyat değişikliği geçmişe dönük çalışmaz.
public class PricingAndBillingFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public PricingAndBillingFlowTests(AbderaWebApplicationFactory factory)
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

    [Fact]
    public async Task Full_billing_flow_create_price_list_fee_plan_receivable_and_payment()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var priceListResponse = await admin.PostAsJsonAsync("/api/price-lists", new PriceLists.CreateRequest(
            "2026-2027 Sezonu", new DateOnly(2026, 8, 1), null,
            [new PriceLists.CreateItemRequest(piano.Id, 45, BillingType.Monthly, 2000m, "TRY", null)]));
        Assert.Equal(HttpStatusCode.Created, priceListResponse.StatusCode);
        var priceList = (await priceListResponse.Content.ReadFromJsonAsync<PriceLists.PriceListResponse>(TestJson.Options))!;
        var item = priceList.Items.Single();

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Bill", "Teacher", [piano.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Bill", "Student", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var feePlanResponse = await admin.PostAsJsonAsync($"/api/enrollments/{enrollment.Id}/fee-plan",
            new FeePlans.CreateRequest(item.Id, DueDay: 5, new DateOnly(2026, 8, 1)));
        Assert.Equal(HttpStatusCode.Created, feePlanResponse.StatusCode);
        var feePlan = (await feePlanResponse.Content.ReadFromJsonAsync<FeePlans.FeePlanResponse>(TestJson.Options))!;
        Assert.Equal(2000m, feePlan.Amount);

        // İkinci bir fee plan aynı kayıt için reddedilmeli (zaten aktif var).
        var duplicateFeePlan = await admin.PostAsJsonAsync($"/api/enrollments/{enrollment.Id}/fee-plan",
            new FeePlans.CreateRequest(item.Id, DueDay: 5, new DateOnly(2026, 8, 1)));
        Assert.Equal(HttpStatusCode.Conflict, duplicateFeePlan.StatusCode);

        var receivableResponse = await admin.PostAsJsonAsync("/api/receivables",
            new Receivables.CreateRequest(enrollment.Id, "2026-09"));
        Assert.Equal(HttpStatusCode.Created, receivableResponse.StatusCode);
        var receivable = (await receivableResponse.Content.ReadFromJsonAsync<Receivables.ReceivableResponse>(TestJson.Options))!;
        Assert.Equal(new DateOnly(2026, 9, 5), receivable.DueDate);
        Assert.Equal(ReceivableStatus.Unpaid, receivable.Status);

        // Aynı dönem için ikinci bir aidat reddedilmeli (UNIQUE enrollment_id+period).
        var duplicateReceivable = await admin.PostAsJsonAsync("/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2026-09"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateReceivable.StatusCode);

        // Kısmi ödeme
        var partialPayment = await admin.PostAsJsonAsync($"/api/receivables/{receivable.Id}/payments",
            new Payments.CreateRequest(800m, new DateOnly(2026, 9, 3), PaymentMethod.Cash, null, "peşinat"));
        Assert.Equal(HttpStatusCode.Created, partialPayment.StatusCode);

        var afterPartial = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == receivable.Id);
        Assert.Equal(ReceivableStatus.Partial, afterPartial.Status);

        // Kalan ödeme
        var finalPayment = await admin.PostAsJsonAsync($"/api/receivables/{receivable.Id}/payments",
            new Payments.CreateRequest(1200m, new DateOnly(2026, 9, 10), PaymentMethod.Transfer, "TR123", null));
        Assert.Equal(HttpStatusCode.Created, finalPayment.StatusCode);

        var afterFull = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == receivable.Id);
        Assert.Equal(ReceivableStatus.Paid, afterFull.Status);

        // Ödenmiş bir aidata yeni ödeme kabul edilmemeli.
        var extraPayment = await admin.PostAsJsonAsync($"/api/receivables/{receivable.Id}/payments",
            new Payments.CreateRequest(100m, new DateOnly(2026, 9, 11), PaymentMethod.Cash, null, null));
        Assert.Equal(HttpStatusCode.Conflict, extraPayment.StatusCode);

        // Öğrenci bazlı toplu görünüm
        var billing = await (await admin.GetAsync($"/api/students/{student.Id}/billing"))
            .Content.ReadFromJsonAsync<List<StudentBilling.StudentBillingResponse>>(TestJson.Options);
        Assert.Single(billing!.Single().Receivables);
    }

    [Fact]
    public async Task Bulk_price_update_does_not_retroactively_change_existing_receivables()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var guitar = instruments!.Single(i => i.Code == "GUITAR");

        var priceListResponse = await admin.PostAsJsonAsync("/api/price-lists", new PriceLists.CreateRequest(
            "Zam Testi Sezonu", new DateOnly(2026, 1, 1), null,
            [new PriceLists.CreateItemRequest(guitar.Id, 60, BillingType.Monthly, 1000m, "TRY", null)]));
        var priceList = (await priceListResponse.Content.ReadFromJsonAsync<PriceLists.PriceListResponse>(TestJson.Options))!;
        var item = priceList.Items.Single();

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Zam", "Teacher", [guitar.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Zam", "Student", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, guitar.Id, new DateOnly(2026, 1, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        await admin.PostAsJsonAsync($"/api/enrollments/{enrollment.Id}/fee-plan",
            new FeePlans.CreateRequest(item.Id, DueDay: 1, new DateOnly(2026, 1, 1)));

        var receivableResponse = await admin.PostAsJsonAsync("/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2026-08"));
        var receivable = (await receivableResponse.Content.ReadFromJsonAsync<Receivables.ReceivableResponse>(TestJson.Options))!;
        Assert.Equal(1000m, receivable.Amount);

        // Önizleme: uygulanmadan önce - miktar değişmemiş olmalı.
        var previewResponse = await admin.PostAsJsonAsync($"/api/price-lists/{priceList.Id}/preview-bulk-update", new BulkUpdate.Request(20));
        var previewBody = await previewResponse.Content.ReadAsStringAsync();
        Assert.True(previewResponse.StatusCode == HttpStatusCode.OK, $"Beklenmeyen durum: {previewResponse.StatusCode}, gövde: {previewBody}");
        var preview = await previewResponse.Content.ReadFromJsonAsync<List<BulkUpdate.ItemPreview>>(TestJson.Options);
        var previewItem = preview!.Single();
        Assert.Equal(1000m, previewItem.OldAmount);
        Assert.Equal(1200m, previewItem.NewAmount);
        Assert.Equal(1, previewItem.ActiveFeePlanCount);

        var itemStillUnchanged = await db.PriceListItems.AsNoTracking().SingleAsync(i => i.Id == item.Id);
        Assert.Equal(1000m, itemStillUnchanged.Amount);

        // Uygula: kalem değişir, geçmiş Receivable değişmez (A1).
        var applyResponse = await admin.PostAsJsonAsync($"/api/price-lists/{priceList.Id}/apply", new BulkUpdate.Request(20));
        var applyBody = await applyResponse.Content.ReadAsStringAsync();
        Assert.True(applyResponse.StatusCode == HttpStatusCode.OK, $"Beklenmeyen durum: {applyResponse.StatusCode}, gövde: {applyBody}");

        var itemAfterApply = await db.PriceListItems.AsNoTracking().SingleAsync(i => i.Id == item.Id);
        Assert.Equal(1200m, itemAfterApply.Amount);

        var receivableAfterApply = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == receivable.Id);
        Assert.Equal(1000m, receivableAfterApply.Amount); // geçmişe dönük değişmedi

        // Yeni bir dönem için üretilen aidat artık yeni fiyatı yansıtmalı (FeePlan hâlâ eski
        // tutarı taşıyor çünkü o da bir snapshot - gerçek dünyada yeni sezon için yeni FeePlan
        // açılır; burada asıl doğrulanan geçmiş Receivable'ın bozulmadığı).
    }

    [Fact]
    public async Task Creating_overlapping_price_list_item_for_same_instrument_duration_and_type_is_rejected()
    {
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var violin = instruments!.Single(i => i.Code == "VIOLIN");

        var first = await admin.PostAsJsonAsync("/api/price-lists", new PriceLists.CreateRequest(
            "Keman A", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            [new PriceLists.CreateItemRequest(violin.Id, 45, BillingType.Monthly, 1800m, "TRY", null)]));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var overlapping = await admin.PostAsJsonAsync("/api/price-lists", new PriceLists.CreateRequest(
            "Keman B", new DateOnly(2026, 6, 1), null,
            [new PriceLists.CreateItemRequest(violin.Id, 45, BillingType.Monthly, 2000m, "TRY", null)]));

        Assert.Equal(HttpStatusCode.Conflict, overlapping.StatusCode);
    }
}
