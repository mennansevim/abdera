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
        var finalPaymentRecord = (await finalPayment.Content.ReadFromJsonAsync<Payments.PaymentResponse>(TestJson.Options))!;

        var afterFull = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == receivable.Id);
        Assert.Equal(ReceivableStatus.Paid, afterFull.Status);

        // Düzeltme özgün ödemeyi silmez; ayrı history/audit kaydı oluşturur ve bakiye
        // etkin tutara göre yeniden hesaplanır.
        var correctionResponse = await admin.PostAsJsonAsync(
            $"/api/payments/{finalPaymentRecord.Id}/corrections",
            new PaymentCorrections.CreateRequest(1000m, "Banka dekontundaki tutar düzeltildi"));
        Assert.Equal(HttpStatusCode.Created, correctionResponse.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(ReceivableStatus.Partial, (await db.Receivables.SingleAsync(r => r.Id == receivable.Id)).Status);
        Assert.Equal(1200m, (await db.Payments.SingleAsync(item => item.Id == finalPaymentRecord.Id)).Amount);
        Assert.True(await db.PaymentCorrections.AnyAsync(item => item.PaymentId == finalPaymentRecord.Id && item.CorrectedAmount == 1000m));
        Assert.True(await db.AuditLogs.AnyAsync(item => item.Action == "payment.corrected" && item.EntityId == finalPaymentRecord.Id));

        var replacementPayment = await admin.PostAsJsonAsync($"/api/receivables/{receivable.Id}/payments",
            new Payments.CreateRequest(200m, new DateOnly(2026, 9, 11), PaymentMethod.Transfer, "TR124", "düzeltme sonrası kalan"));
        Assert.Equal(HttpStatusCode.Created, replacementPayment.StatusCode);

        // Ödenmiş bir aidata yeni ödeme kabul edilmemeli.
        var extraPayment = await admin.PostAsJsonAsync($"/api/receivables/{receivable.Id}/payments",
            new Payments.CreateRequest(100m, new DateOnly(2026, 9, 11), PaymentMethod.Cash, null, null));
        Assert.Equal(HttpStatusCode.Conflict, extraPayment.StatusCode);

        // Öğrenci bazlı toplu görünüm
        var billing = await (await admin.GetAsync($"/api/students/{student.Id}/billing"))
            .Content.ReadFromJsonAsync<List<StudentBilling.StudentBillingResponse>>(TestJson.Options);
        Assert.Single(billing!.Single().Receivables);

        var dueList = await (await admin.GetAsync("/api/billing/dues"))
            .Content.ReadFromJsonAsync<List<StudentBilling.DueListItemResponse>>(TestJson.Options);
        var listedDue = dueList!.Single(item => item.Id == receivable.Id);
        Assert.Equal("Bill Student", listedDue.StudentName);
        Assert.Equal("Piyano", listedDue.InstrumentName);
        Assert.Equal(2000m, listedDue.TotalPaid);
        Assert.Contains(listedDue.Payments, item => item.Kind == "Correction" && item.CorrectsPaymentId == finalPaymentRecord.Id);
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

    // Faz 2: toplu ödeme (10/12 aylık) - seçilen aydan başlayarak istenen ay sayısı kadar
    // Receivable oluşturup/tutup tek transaction'da ödemeyi dağıtır (BulkPayments.cs).
    [Fact]
    public async Task Bulk_payment_distributes_amount_across_requested_months_and_marks_them_paid()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var drums = instruments!.Single(i => i.Code == "DRUMS");

        var priceListResponse = await admin.PostAsJsonAsync("/api/price-lists", new PriceLists.CreateRequest(
            "Toplu Ödeme Sezonu", new DateOnly(2026, 1, 1), null,
            [new PriceLists.CreateItemRequest(drums.Id, 45, BillingType.Monthly, 1000m, "TRY", null)]));
        var priceList = (await priceListResponse.Content.ReadFromJsonAsync<PriceLists.PriceListResponse>(TestJson.Options))!;
        var item = priceList.Items.Single();

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Toplu", "Öğretmen", [drums.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Toplu", "Öğrenci", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, drums.Id, new DateOnly(2026, 1, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        await admin.PostAsJsonAsync($"/api/enrollments/{enrollment.Id}/fee-plan",
            new FeePlans.CreateRequest(item.Id, DueDay: 1, new DateOnly(2026, 1, 1)));

        // 3 aylık (3000 TRY) toplu ödeme - bu ayların hiçbirinin Receivable'ı henüz yok,
        // BulkPayments bunları kendisi oluşturmalı.
        var bulkResponse = await admin.PostAsJsonAsync($"/api/enrollments/{enrollment.Id}/bulk-payments",
            new BulkPayments.CreateRequest(enrollment.Id, "2026-09", 3, 3000m, new DateOnly(2026, 9, 1), PaymentMethod.Transfer, "toplu-1", null));
        var bulkBody = await bulkResponse.Content.ReadAsStringAsync();
        Assert.True(bulkResponse.StatusCode == HttpStatusCode.OK, $"Beklenmeyen durum: {bulkResponse.StatusCode}, gövde: {bulkBody}");
        var receivables = await bulkResponse.Content.ReadFromJsonAsync<List<Receivables.ReceivableResponse>>(TestJson.Options);
        Assert.Equal(3, receivables!.Count);
        Assert.All(receivables, r => Assert.Equal(ReceivableStatus.Paid, r.Status));
        Assert.Equal(["2026-09", "2026-10", "2026-11"], receivables.Select(r => r.Period).OrderBy(p => p));

        var storedReceivables = await db.Receivables.AsNoTracking()
            .Where(r => r.EnrollmentId == enrollment.Id).ToListAsync();
        Assert.Equal(3, storedReceivables.Count);
        Assert.All(storedReceivables, r => Assert.Equal(ReceivableStatus.Paid, r.Status));
        // Fiyat snapshot'ı korunmalı - her Receivable feePlan'ın o anki tutarını taşımalı.
        Assert.All(storedReceivables, r => Assert.Equal(1000m, r.Amount));
    }

    // Dönem başında tüm aktif kayıtların aidatını tek çağrıda açar (BulkReceivables.cs).
    // Ücret planı olmayan kayıt sessizce atlanmaz - "eksikler" listesinde geri döner.
    [Fact]
    public async Task Bulk_receivables_create_dues_for_active_enrollments_and_report_missing_fee_plans()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var guitar = instruments!.Single(i => i.Code == "GUITAR");

        var priceListResponse = await admin.PostAsJsonAsync("/api/price-lists", new PriceLists.CreateRequest(
            "Toplu Aidat Sezonu", new DateOnly(2026, 1, 1), null,
            [new PriceLists.CreateItemRequest(guitar.Id, 45, BillingType.Monthly, 1500m, "TRY", null)]));
        var priceList = (await priceListResponse.Content.ReadFromJsonAsync<PriceLists.PriceListResponse>(TestJson.Options))!;
        var item = priceList.Items.Single();

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Toplu", "Aidat Öğretmeni", [guitar.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;

        // İki öğrenci: birinin ücret planı var (aidatı açılmalı), diğerinin yok (eksik listesine düşmeli).
        var withPlan = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Planlı", "Öğrenci", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var withoutPlan = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Plansız", "Öğrenci", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        var plannedEnrollment = (await (await admin.PostAsJsonAsync($"/api/students/{withPlan.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, guitar.Id, new DateOnly(2026, 1, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
        var unplannedEnrollment = (await (await admin.PostAsJsonAsync($"/api/students/{withoutPlan.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, guitar.Id, new DateOnly(2026, 1, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        await admin.PostAsJsonAsync($"/api/enrollments/{plannedEnrollment.Id}/fee-plan",
            new FeePlans.CreateRequest(item.Id, DueDay: 5, new DateOnly(2026, 1, 1)));

        const string period = "2027-03";
        var preview = await (await admin.GetAsync($"/api/receivables/bulk-preview?period={period}"))
            .Content.ReadFromJsonAsync<BulkReceivables.PlanResponse>(TestJson.Options);
        Assert.Contains(preview!.Ready, row => row.EnrollmentId == plannedEnrollment.Id);
        Assert.Contains(preview.Missing, row => row.EnrollmentId == unplannedEnrollment.Id);

        var createResponse = await admin.PostAsJsonAsync("/api/receivables/bulk", new BulkReceivables.CreateRequest(period));
        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.True(createResponse.StatusCode == HttpStatusCode.OK, $"Beklenmeyen durum: {createResponse.StatusCode}, gövde: {createBody}");
        var created = (await createResponse.Content.ReadFromJsonAsync<BulkReceivables.CreateResponse>(TestJson.Options))!;
        Assert.True(created.CreatedCount >= 1);
        Assert.Contains(created.Missing, row => row.EnrollmentId == unplannedEnrollment.Id);

        var stored = await db.Receivables.AsNoTracking()
            .SingleAsync(receivable => receivable.EnrollmentId == plannedEnrollment.Id && receivable.Period == period);
        Assert.Equal(1500m, stored.Amount);
        Assert.Equal(new DateOnly(2027, 3, 5), stored.DueDate);
        Assert.False(await db.Receivables.AsNoTracking()
            .AnyAsync(receivable => receivable.EnrollmentId == unplannedEnrollment.Id && receivable.Period == period));

        // Aynı dönem ikinci kez çalıştırılırsa mükerrer aidat üretilmemeli: açılacak yeni
        // kayıt kalmadığı için istek 409 döner ve tablo değişmez.
        var repeat = await admin.PostAsJsonAsync("/api/receivables/bulk", new BulkReceivables.CreateRequest(period));
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Equal(1, await db.Receivables.AsNoTracking()
            .CountAsync(receivable => receivable.EnrollmentId == plannedEnrollment.Id && receivable.Period == period));
    }

    [Fact]
    public async Task Bulk_receivables_reject_malformed_period()
    {
        var admin = await CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/receivables/bulk", new BulkReceivables.CreateRequest("2027/03"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_payment_exceeding_outstanding_balance_of_requested_months_is_rejected()
    {
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var drums = instruments!.Single(i => i.Code == "DRUMS");

        var priceListResponse = await admin.PostAsJsonAsync("/api/price-lists", new PriceLists.CreateRequest(
            "Toplu Ödeme Hata Sezonu", new DateOnly(2026, 1, 1), null,
            [new PriceLists.CreateItemRequest(drums.Id, 60, BillingType.Monthly, 1000m, "TRY", null)]));
        var priceList = (await priceListResponse.Content.ReadFromJsonAsync<PriceLists.PriceListResponse>(TestJson.Options))!;
        var item = priceList.Items.Single();

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Hata", "Öğretmen", [drums.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Hata", "Öğrenci", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, drums.Id, new DateOnly(2026, 1, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        await admin.PostAsJsonAsync($"/api/enrollments/{enrollment.Id}/fee-plan",
            new FeePlans.CreateRequest(item.Id, DueDay: 1, new DateOnly(2026, 1, 1)));

        // 1 aylık borç (1000 TRY) için 5000 TRY gönderiliyor - fazlası hiçbir aidata sayılmamalı,
        // istek reddedilmeli (BulkPayments.cs: "remaining > 0.01m" kontrolü).
        var bulkResponse = await admin.PostAsJsonAsync($"/api/enrollments/{enrollment.Id}/bulk-payments",
            new BulkPayments.CreateRequest(enrollment.Id, "2026-09", 1, 5000m, new DateOnly(2026, 9, 1), PaymentMethod.Transfer, null, null));

        Assert.Equal(HttpStatusCode.Conflict, bulkResponse.StatusCode);
    }
}
