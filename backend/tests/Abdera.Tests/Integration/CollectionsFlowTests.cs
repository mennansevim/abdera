using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Aidatlar ekranının tek tahsilat yolu (Collections.cs, docs/10-decisions.md H17) ve ekranın
// veri kaynağı (BillingBoard.cs). Tarife migration ile seed edilir: Birebir 6.000, kardeş %5,
// çoklu kurs %5, peşin 4 ay %5 / 10 ay %10. Dönemler 2099'da: kayıt TestPeriods.StartsInLaterPeriod
// ile başlar, otomatik açılan "bu ayın aidatı" teste karışmaz.
public class CollectionsFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public CollectionsFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Beklenmeyen durum: {(int)response.StatusCode} {response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private async Task<(Students.StudentResponse Student, Enrollments.EnrollmentResponse Enrollment)> EnrollAsync(
        HttpClient admin, string name, bool sibling = false)
    {
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(instrument => instrument.Code == "PIANO").Id;
        var teacher = (await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/teachers", new Teachers.CreateRequest(name, "Ogretmen", [piano], null)))).Teacher;
        var student = await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync(
            "/api/students", new Students.CreateRequest(name, "Tahsilat", new DateOnly(2014, 1, 1), sibling)));
        var enrollment = await ReadAsync<Enrollments.EnrollmentResponse>(await admin.PostAsJsonAsync(
            $"/api/students/{student.Id}/enrollments",
            new Enrollments.CreateRequest(teacher.Id, piano, TestPeriods.StartsInLaterPeriod, CourseKind.Individual)));
        return (student, enrollment);
    }

    private static Task<HttpResponseMessage> CollectAsync(HttpClient client, Guid enrollmentId, Collections.CreateRequest request, string? key = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/enrollments/{enrollmentId}/collections")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return client.SendAsync(message);
    }

    [Fact]
    public async Task Selected_months_are_priced_with_the_chosen_discounts_and_settled_in_one_go()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await AdminAsync();
        var (_, enrollment) = await EnrollAsync(admin, "Secim", sibling: true);

        // Ocak normal tarifeyle (kardeş %5) açılmış, ödenmemiş.
        var opened = await ReadAsync<Receivables.ReceivableResponse>(await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2099-01")));
        Assert.Equal(5700m, opened.Amount);

        // Varsayılanlar öğrencinin olgularından gelir: kardeş işaretli, tek kurs.
        var defaults = await ReadAsync<Collections.Quote>(await admin.GetAsync(
            $"/api/enrollments/{enrollment.Id}/collection-preview?periods=2099-01"));
        Assert.True(defaults.Defaults.Sibling);
        Assert.False(defaults.Defaults.MultiCourse);

        // Yönetici kardeş indirimini KAPATIP 4 ayı peşin alıyor: 4 x 6.000 x 0,95 = 22.800.
        const string query = "periods=2099-01&periods=2099-02&periods=2099-03&periods=2099-04&sibling=false&multiCourse=false&prepay=true";
        var quote = await ReadAsync<Collections.Quote>(await admin.GetAsync($"/api/enrollments/{enrollment.Id}/collection-preview?{query}"));
        Assert.Equal(5m, quote.PrepayPercent);
        Assert.Equal(22800m, quote.Total);
        Assert.Empty(quote.Blockers);
        Assert.True(quote.Rows.Single(row => row.Period == "2099-01").Repriced);

        var created = await ReadAsync<Collections.CreateResponse>(await CollectAsync(admin, enrollment.Id, new Collections.CreateRequest(
            ["2099-04", "2099-01", "2099-02", "2099-03"],
            new Collections.DiscountChoice(Sibling: false, MultiCourse: false, Prepay: true, ManualPercent: null),
            new DateOnly(2099, 1, 3), PaymentMethod.Cash, null, ExpectedTotal: quote.Total)));

        Assert.Equal(22800m, created.Total);
        Assert.NotNull(created.PrepayPlanId);
        Assert.Equal(4, created.Receivables.Count);
        Assert.All(created.Receivables, receivable =>
        {
            Assert.Equal(ReceivableStatus.Paid, receivable.Status);
            Assert.Equal(5700m, receivable.Amount);
            Assert.Equal(created.PrepayPlanId, receivable.PrepayPlanId);
        });

        // Açılmış ay yeniden fiyatlandı, ikinci bir satır açılmadı; indirimin gerekçesi artık peşin.
        var january = await db.Receivables.AsNoTracking().SingleAsync(r => r.EnrollmentId == enrollment.Id && r.Period == "2099-01");
        Assert.Equal(opened.Id, january.Id);
        Assert.Contains("Peşin", january.DiscountReason);
        Assert.DoesNotContain("Kardeş", january.DiscountReason);
        Assert.Equal(4, await db.AuditLogs.CountAsync(log => log.Action == "receivable.collection_recorded"
            && created.Receivables.Select(r => r.Id).Contains(log.EntityId)));
    }

    [Fact]
    public async Task One_tap_collection_pays_the_frozen_amount_and_a_retry_does_not_pay_twice()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await AdminAsync();
        var (_, enrollment) = await EnrollAsync(admin, "Tekdokunus", sibling: true);
        var opened = await ReadAsync<Receivables.ReceivableResponse>(await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2099-05")));

        var request = new Collections.CreateRequest(["2099-05"], null, new DateOnly(2099, 5, 2), PaymentMethod.Cash, null, ExpectedTotal: opened.Amount);
        const string key = "tek-dokunus-2099-05";
        var first = await ReadAsync<Collections.CreateResponse>(await CollectAsync(admin, enrollment.Id, request, key));
        var retry = await ReadAsync<Collections.CreateResponse>(await CollectAsync(admin, enrollment.Id, request, key));

        Assert.Equal(5700m, first.Total);
        Assert.Null(first.PrepayPlanId);
        Assert.True(retry.Replayed);
        Assert.Equal(1, await db.Payments.CountAsync(payment => payment.ReceivableId == opened.Id));
        var stored = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == opened.Id);
        Assert.Equal(ReceivableStatus.Paid, stored.Status);
        Assert.Equal(5700m, stored.Amount);
    }

    [Fact]
    public async Task A_paid_month_or_a_stale_screen_total_stops_the_whole_collection()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await AdminAsync();
        var (_, enrollment) = await EnrollAsync(admin, "Durdur");
        (await CollectAsync(admin, enrollment.Id, new Collections.CreateRequest(
            ["2099-06"], null, new DateOnly(2099, 6, 1), PaymentMethod.Cash, null, null))).EnsureSuccessStatusCode();

        var withPaid = await CollectAsync(admin, enrollment.Id, new Collections.CreateRequest(
            ["2099-06", "2099-07"], new Collections.DiscountChoice(false, false, false, null),
            new DateOnly(2099, 7, 1), PaymentMethod.Cash, null, null));
        Assert.Equal(HttpStatusCode.Conflict, withPaid.StatusCode);

        var stale = await CollectAsync(admin, enrollment.Id, new Collections.CreateRequest(
            ["2099-07"], new Collections.DiscountChoice(false, false, false, null),
            new DateOnly(2099, 7, 1), PaymentMethod.Cash, null, ExpectedTotal: 1m));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // İkisi de hiçbir şey yazmadı: Temmuz hiç açılmadı.
        Assert.False(await db.Receivables.AnyAsync(r => r.EnrollmentId == enrollment.Id && r.Period == "2099-07"));
    }

    [Fact]
    public async Task A_partially_paid_month_is_not_repriced_and_only_its_remainder_is_collected()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await AdminAsync();
        var (_, enrollment) = await EnrollAsync(admin, "Kismi");
        var opened = await ReadAsync<Receivables.ReceivableResponse>(await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2099-08")));
        var partial = new HttpRequestMessage(HttpMethod.Post, $"/api/receivables/{opened.Id}/payments")
        {
            Content = JsonContent.Create(new Payments.CreateRequest(2000m, new DateOnly(2099, 8, 1), PaymentMethod.Cash, null, null)),
        };
        partial.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await admin.SendAsync(partial)).EnsureSuccessStatusCode();

        // %20 özel indirim seçilse de ödeme görmüş ay donmuş tutarında kalır: 6.000 - 2.000.
        var quote = await ReadAsync<Collections.Quote>(await admin.GetAsync(
            $"/api/enrollments/{enrollment.Id}/collection-preview?periods=2099-08&sibling=false&multiCourse=false&prepay=false&manualPercent=20"));
        Assert.Equal(4000m, quote.Total);
        Assert.False(quote.Rows.Single().Repriced);

        var created = await ReadAsync<Collections.CreateResponse>(await CollectAsync(admin, enrollment.Id, new Collections.CreateRequest(
            ["2099-08"], new Collections.DiscountChoice(false, false, false, 20m),
            new DateOnly(2099, 8, 9), PaymentMethod.Transfer, null, ExpectedTotal: 4000m)));
        Assert.Equal(4000m, created.Total);
        var stored = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == opened.Id);
        Assert.Equal(6000m, stored.Amount);
        Assert.Equal(ReceivableStatus.Paid, stored.Status);
    }

    [Fact]
    public async Task A_partial_collection_leaves_the_rest_owed_and_is_only_allowed_for_a_single_month()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await AdminAsync();
        var (_, enrollment) = await EnrollAsync(admin, "Yarim");

        var created = await ReadAsync<Collections.CreateResponse>(await CollectAsync(admin, enrollment.Id, new Collections.CreateRequest(
            ["2099-09"], new Collections.DiscountChoice(false, false, false, null),
            new DateOnly(2099, 9, 2), PaymentMethod.Cash, null, ExpectedTotal: 6000m, PartialAmount: 2500m)));
        Assert.Equal(2500m, created.Total);
        var stored = await db.Receivables.AsNoTracking().SingleAsync(r => r.EnrollmentId == enrollment.Id && r.Period == "2099-09");
        Assert.Equal(ReceivableStatus.Partial, stored.Status);
        Assert.Equal(6000m, stored.Amount);

        var twoMonths = await CollectAsync(admin, enrollment.Id, new Collections.CreateRequest(
            ["2099-10", "2099-11"], new Collections.DiscountChoice(false, false, false, null),
            new DateOnly(2099, 10, 2), PaymentMethod.Cash, null, null, PartialAmount: 1000m));
        Assert.Equal(HttpStatusCode.BadRequest, twoMonths.StatusCode);

        var notLess = await CollectAsync(admin, enrollment.Id, new Collections.CreateRequest(
            ["2099-09"], null, new DateOnly(2099, 9, 3), PaymentMethod.Cash, null, null, PartialAmount: 3500m));
        Assert.Equal(HttpStatusCode.BadRequest, notLess.StatusCode);
    }

    [Fact]
    public async Task Board_lists_every_enrollment_with_its_months_of_the_year()
    {
        var admin = await AdminAsync();
        var (student, enrollment) = await EnrollAsync(admin, "Cizelge", sibling: true);
        (await CollectAsync(admin, enrollment.Id, new Collections.CreateRequest(
            ["2099-02"], null, new DateOnly(2099, 2, 4), PaymentMethod.Cash, null, null))).EnsureSuccessStatusCode();
        await ReadAsync<Receivables.ReceivableResponse>(await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2099-03")));

        var board = await ReadAsync<List<BillingBoard.BoardRow>>(await admin.GetAsync("/api/billing/board?year=2099"));
        var row = board.Single(item => item.EnrollmentId == enrollment.Id);
        Assert.Equal(student.Id, row.StudentId);
        Assert.True(row.SiblingDiscount);
        Assert.Equal(["2099-02", "2099-03"], row.Cells.Select(cell => cell.Period));
        Assert.Equal(ReceivableStatus.Paid, row.Cells[0].Status);
        Assert.Equal(new DateOnly(2099, 2, 4), row.Cells[0].LastPaymentDate);
        Assert.Equal(ReceivableStatus.Unpaid, row.Cells[1].Status);

        // Başka bir yıl bu kaydın aylarını taşımaz (kayıt 2099'da başlıyor).
        var earlier = await ReadAsync<List<BillingBoard.BoardRow>>(await admin.GetAsync("/api/billing/board?year=2098"));
        Assert.DoesNotContain(earlier, item => item.EnrollmentId == enrollment.Id);
    }
}
