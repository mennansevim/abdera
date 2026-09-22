using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Yeniden tasarlanan aidat akışı (docs/10-decisions.md H1). Tarife ve indirim politikası
// migration ile seed edilir (Birebir 6.000, Grup 4.500, %5/%5, peşin 4 ay %5 / 10 ay %10),
// bu yüzden testler "önce fiyat listesi kur, sonra ücret planı aç" adımlarını hiç içermez -
// zaten yeni modelin amacı bu adımları ortadan kaldırmaktı.
//
// CLAUDE.md: OrderBy'ı record projeksiyonundan SONRA koyan sorgular yalnızca gerçek bir HTTP
// çağrısında patlar; bu yüzden her yeni handler burada gerçekten HTTP üzerinden çağrılıyor.
public class TuitionAndDuesFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;
    private static int _phoneSeed = 1000;

    public TuitionAndDuesFlowTests(AbderaWebApplicationFactory factory)
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

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Beklenmeyen durum: {(int)response.StatusCode} {response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private async Task<Guid> InstrumentIdAsync(HttpClient admin, string code)
    {
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        return instruments.Single(instrument => instrument.Code == code).Id;
    }

    private async Task<Teachers.TeacherResponse> CreateTeacherAsync(HttpClient admin, string name, params Guid[] instrumentIds) =>
        (await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/teachers", new Teachers.CreateRequest(name, "Ogretmen", instrumentIds, null)))).Teacher;

    private async Task<Students.StudentResponse> CreateStudentAsync(HttpClient admin, string name) =>
        await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync(
            "/api/students", new Students.CreateRequest(name, "Ogrenci", new DateOnly(2014, 1, 1))));

    private async Task<Enrollments.EnrollmentResponse> EnrollAsync(
        HttpClient admin, Guid studentId, Guid teacherId, Guid instrumentId, CourseKind kind = CourseKind.Individual) =>
        await ReadAsync<Enrollments.EnrollmentResponse>(await admin.PostAsJsonAsync(
            $"/api/students/{studentId}/enrollments",
            new Enrollments.CreateRequest(teacherId, instrumentId, new DateOnly(2026, 9, 1), kind)));

    private async Task LinkSiblingsAsync(HttpClient admin, params Guid[] studentIds)
    {
        var phone = $"+90532{Interlocked.Increment(ref _phoneSeed):D7}";
        var guardian = await ReadAsync<Guardians.GuardianResponse>(await admin.PostAsJsonAsync(
            "/api/guardians", new Guardians.CreateRequest("Ortak", "Veli", phone)));

        foreach (var studentId in studentIds)
        {
            var response = await admin.PostAsJsonAsync(
                $"/api/students/{studentId}/guardians",
                new LinkGuardianToStudent.Request(guardian.Id, "Anne", IsPrimary: true));
            response.EnsureSuccessStatusCode();
        }
    }

    // --- Ay başı üretimi ----------------------------------------------------------

    [Fact]
    public async Task Monthly_run_prices_each_course_from_the_tariff_and_applies_the_right_discount()
    {
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var art = await InstrumentIdAsync(admin, "ART");
        var teacher = await CreateTeacherAsync(admin, "Tarife", piano, art);

        // Tek kurs, indirimsiz -> tam tarife.
        var plain = await CreateStudentAsync(admin, "Sade");
        var plainEnrollment = await EnrollAsync(admin, plain.Id, teacher.Id, piano);

        // İki kursa giden öğrenci -> %5.
        var twoCourses = await CreateStudentAsync(admin, "Ikikurs");
        var firstCourse = await EnrollAsync(admin, twoCourses.Id, teacher.Id, piano);
        await EnrollAsync(admin, twoCourses.Id, teacher.Id, art, CourseKind.Group);

        // Ortak velili iki kardeş -> %5.
        var siblingA = await CreateStudentAsync(admin, "KardesA");
        var siblingB = await CreateStudentAsync(admin, "KardesB");
        var siblingEnrollment = await EnrollAsync(admin, siblingA.Id, teacher.Id, piano);
        await EnrollAsync(admin, siblingB.Id, teacher.Id, piano);
        await LinkSiblingsAsync(admin, siblingA.Id, siblingB.Id);

        var plan = await ReadAsync<MonthlyDueRun.PlanResponse>(
            await admin.GetAsync("/api/receivables/monthly-run?period=2026-09"));

        var plainRow = plan.Ready.Single(row => row.EnrollmentId == plainEnrollment.Id);
        Assert.Equal(6000m, plainRow.BaseAmount);
        Assert.Equal(0m, plainRow.DiscountPercent);
        Assert.Equal(6000m, plainRow.Amount);

        var multiRow = plan.Ready.Single(row => row.EnrollmentId == firstCourse.Id);
        Assert.Equal(5m, multiRow.DiscountPercent);
        Assert.Equal(5700m, multiRow.Amount);
        Assert.Equal("2 kurs indirimi (%5)", multiRow.DiscountReason);

        // Grup dersi farklı tarifeden fiyatlanır ve aynı indirimi alır.
        var groupRow = plan.Ready.Single(row =>
            row.StudentId == twoCourses.Id && row.CourseKind == CourseKind.Group);
        Assert.Equal(4500m, groupRow.BaseAmount);
        Assert.Equal(4275m, groupRow.Amount);

        var siblingRow = plan.Ready.Single(row => row.EnrollmentId == siblingEnrollment.Id);
        Assert.Equal(5m, siblingRow.DiscountPercent);
        Assert.Equal("Kardeş indirimi (%5)", siblingRow.DiscountReason);

        Assert.Equal(new DateOnly(2026, 9, 1), plan.DueDate);
        Assert.Equal(plan.ReadyBaseTotal - plan.ReadyTotal, plan.ReadyDiscountTotal);
    }

    [Fact]
    public async Task Monthly_run_writes_what_the_preview_promised_and_is_idempotent()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var teacher = await CreateTeacherAsync(admin, "Uretim", piano);
        var student = await CreateStudentAsync(admin, "Uretim");
        var enrollment = await EnrollAsync(admin, student.Id, teacher.Id, piano);

        var preview = await ReadAsync<MonthlyDueRun.PlanResponse>(
            await admin.GetAsync("/api/receivables/monthly-run?period=2026-10"));
        var expected = preview.Ready.Single(row => row.EnrollmentId == enrollment.Id);

        var created = await ReadAsync<MonthlyDueRun.CreateResponse>(
            await admin.PostAsJsonAsync("/api/receivables/monthly-run", new MonthlyDueRun.CreateRequest("2026-10")));
        Assert.Equal(preview.Ready.Count, created.CreatedCount);

        var written = await db.Receivables.AsNoTracking()
            .SingleAsync(receivable => receivable.EnrollmentId == enrollment.Id && receivable.Period == "2026-10");
        Assert.Equal(expected.Amount, written.Amount);
        Assert.Equal(expected.BaseAmount, written.BaseAmount);
        Assert.Equal(new DateOnly(2026, 10, 1), written.DueDate);
        Assert.Equal(ReceivableStatus.Unpaid, written.Status);
        Assert.True(await db.AuditLogs.AnyAsync(log =>
            log.Action == "receivable.monthly_run_created" && log.EntityId == written.Id));

        // İkinci kez çalıştırmak aynı ayı ikilemez - tümü "zaten var"a düşer.
        var second = await admin.PostAsJsonAsync(
            "/api/receivables/monthly-run", new MonthlyDueRun.CreateRequest("2026-10"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var rerun = await ReadAsync<MonthlyDueRun.PlanResponse>(
            await admin.GetAsync("/api/receivables/monthly-run?period=2026-10"));
        Assert.Contains(rerun.AlreadyExists, row => row.EnrollmentId == enrollment.Id);
        Assert.DoesNotContain(rerun.Ready, row => row.EnrollmentId == enrollment.Id);
    }

    [Fact]
    public async Task Monthly_run_reports_courses_without_a_valid_tariff_instead_of_billing_zero()
    {
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var teacher = await CreateTeacherAsync(admin, "Tarifesiz", piano);
        var student = await CreateStudentAsync(admin, "Tarifesiz");
        var enrollment = await EnrollAsync(admin, student.Id, teacher.Id, piano);

        // Tarife 2026-09'da yürürlüğe giriyor; ondan önceki bir dönemde geçerli tarife yok.
        var plan = await ReadAsync<MonthlyDueRun.PlanResponse>(
            await admin.GetAsync("/api/receivables/monthly-run?period=2026-08"));

        Assert.Empty(plan.Ready);
        var missing = plan.Missing.Single(row => row.EnrollmentId == enrollment.Id);
        Assert.Contains("tarife", missing.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Monthly_run_rejects_a_malformed_period()
    {
        var admin = await CreateAdminClientAsync();

        var response = await admin.GetAsync("/api/receivables/monthly-run?period=eylul");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Tahsilat ve düzeltme -----------------------------------------------------

    [Fact]
    public async Task Single_due_supports_partial_payment_correction_and_full_settlement()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var teacher = await CreateTeacherAsync(admin, "Tahsilat", piano);
        var student = await CreateStudentAsync(admin, "Tahsilat");
        var enrollment = await EnrollAsync(admin, student.Id, teacher.Id, piano);

        var receivable = await ReadAsync<Receivables.ReceivableResponse>(await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2026-11")));
        Assert.Equal(6000m, receivable.Amount);
        Assert.Equal(new DateOnly(2026, 11, 1), receivable.DueDate);

        // Aynı dönem için ikinci aidat reddedilir (UNIQUE enrollment_id + period).
        var duplicate = await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2026-11"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var partial = await ReadAsync<Payments.PaymentResponse>(await admin.PostAsJsonAsync(
            $"/api/receivables/{receivable.Id}/payments",
            new Payments.CreateRequest(2000m, new DateOnly(2026, 11, 3), PaymentMethod.Cash, null, "peşinat")));
        Assert.Equal(ReceivableStatus.Partial,
            (await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == receivable.Id)).Status);

        var rest = await ReadAsync<Payments.PaymentResponse>(await admin.PostAsJsonAsync(
            $"/api/receivables/{receivable.Id}/payments",
            new Payments.CreateRequest(4000m, new DateOnly(2026, 11, 10), PaymentMethod.Transfer, "TR123", null)));
        db.ChangeTracker.Clear();
        Assert.Equal(ReceivableStatus.Paid,
            (await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == receivable.Id)).Status);

        // Düzeltme özgün ödemeyi silmez, ayrı bir satır yazar ve bakiyeyi geri açar.
        var correction = await admin.PostAsJsonAsync(
            $"/api/payments/{rest.Id}/corrections",
            new PaymentCorrections.CreateRequest(3000m, "Dekont tutarı düzeltildi"));
        Assert.Equal(HttpStatusCode.Created, correction.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(ReceivableStatus.Partial,
            (await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == receivable.Id)).Status);
        Assert.Equal(4000m, (await db.Payments.AsNoTracking().SingleAsync(p => p.Id == rest.Id)).Amount);
        Assert.NotEqual(Guid.Empty, partial.Id);

        // Aidat listesi indirim alanlarını da taşımalı (gerçek HTTP çağrısı - projeksiyon
        // sonrası OrderBy hatası ancak böyle yakalanır).
        var dues = await ReadAsync<List<StudentBilling.DueListItemResponse>>(
            await admin.GetAsync("/api/billing/dues"));
        var listed = dues.Single(item => item.Id == receivable.Id);
        Assert.Equal(6000m, listed.BaseAmount);
        Assert.Equal(CourseKind.Individual, listed.CourseKind);
        Assert.Contains(listed.Payments, item => item.Kind == "Correction");
    }

    // --- Yıl başı peşin ödeme kampanyası ------------------------------------------

    // Toplu ödeme ekranı öğrenciyi /api/students/search ile bulur ve seçtiği satırın
    // EnrollmentId'siyle doğrudan prepay-preview'a gider. Arama satır başına KURS KAYDI
    // döndürmezse ekran ikinci bir isteğe muhtaç kalır; bu test o sözleşmeyi korur.
    [Fact]
    public async Task Student_search_returns_the_enrollment_a_bulk_payment_can_start_from()
    {
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var art = await InstrumentIdAsync(admin, "ART");
        var teacher = await CreateTeacherAsync(admin, "Arama", piano, art);
        var student = await CreateStudentAsync(admin, "Aranan");
        var pianoEnrollment = await EnrollAsync(admin, student.Id, teacher.Id, piano);
        var artEnrollment = await EnrollAsync(admin, student.Id, teacher.Id, art, CourseKind.Group);

        var rows = await ReadAsync<List<Students.StudentSearchResponse>>(
            await admin.GetAsync("/api/students/search?query=Aranan"));

        // Aynı öğrencinin iki kursu AYRI satırlar: fiyatın tek ekseni CourseKind olduğu
        // için toplu ödemede hangi kursun ödendiği seçilebilmeli.
        var mine = rows.Where(row => row.StudentId == student.Id).ToList();
        Assert.Equal(2, mine.Count);
        Assert.Equal(CourseKind.Individual, mine.Single(row => row.EnrollmentId == pianoEnrollment.Id).CourseKind);
        Assert.Equal(CourseKind.Group, mine.Single(row => row.EnrollmentId == artEnrollment.Id).CourseKind);

        var preview = await ReadAsync<PrepayPlans.PreviewResponse>(await admin.GetAsync(
            $"/api/enrollments/{mine.Single(row => row.EnrollmentId == pianoEnrollment.Id).EnrollmentId}/prepay-preview?startPeriod=2027-03&months=3"));
        Assert.Equal(pianoEnrollment.Id, preview.EnrollmentId);
        Assert.Equal(3, preview.MonthRows.Count);
    }

    [Fact]
    public async Task Prepay_plan_stacks_the_campaign_discount_on_top_and_settles_every_month()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var art = await InstrumentIdAsync(admin, "ART");
        var teacher = await CreateTeacherAsync(admin, "Pesin", piano, art);
        var student = await CreateStudentAsync(admin, "Pesin");
        var enrollment = await EnrollAsync(admin, student.Id, teacher.Id, piano);
        await EnrollAsync(admin, student.Id, teacher.Id, art, CourseKind.Group); // %5 çoklu kurs

        var preview = await ReadAsync<PrepayPlans.PreviewResponse>(await admin.GetAsync(
            $"/api/enrollments/{enrollment.Id}/prepay-preview?startPeriod=2027-01&months=10"));

        Assert.Equal(10m, preview.PrepayPercent);
        Assert.Equal(5m, preview.StudentDiscountPercent);
        Assert.Equal(60000m, preview.BaseTotal);              // 10 x 6.000
        Assert.Equal(51300m, preview.Total);                  // 10 x (6.000 x 0,95 x 0,90)
        Assert.Equal(8700m, preview.SavingTotal);
        Assert.Equal(10, preview.MonthRows.Count);
        Assert.Empty(preview.Blockers);

        var created = await ReadAsync<PrepayPlans.CreateResponse>(await admin.PostAsJsonAsync(
            $"/api/enrollments/{enrollment.Id}/prepay-plans",
            new PrepayPlans.CreateRequest(
                "2027-01", 10, new DateOnly(2027, 1, 5), PaymentMethod.Transfer, "TR-PESIN", "sezon peşin",
                ExpectedTotal: preview.Total)));

        Assert.Equal(51300m, created.Total);
        Assert.Equal(10, created.Receivables.Count);
        Assert.All(created.Receivables, receivable =>
        {
            Assert.Equal(ReceivableStatus.Paid, receivable.Status);
            Assert.Equal(5130m, receivable.Amount);
            Assert.Equal(14.5m, receivable.DiscountPercent);
            Assert.Equal(created.PrepayPlanId, receivable.PrepayPlanId);
        });

        var stored = await db.Receivables.AsNoTracking()
            .Where(receivable => receivable.EnrollmentId == enrollment.Id && receivable.PrepayPlanId != null)
            .ToListAsync();
        Assert.Equal(10, stored.Count);
        Assert.Equal(51300m, stored.Sum(receivable => receivable.Amount));

        var payments = await db.Payments.AsNoTracking()
            .Where(payment => payment.PrepayPlanId == created.PrepayPlanId)
            .ToListAsync();
        Assert.Equal(10, payments.Count);
        Assert.All(payments, payment => Assert.Equal(10, payment.PrepayPlanMonths));
        Assert.Equal(51300m, payments.Sum(payment => payment.Amount));
    }

    [Fact]
    public async Task Prepay_plan_reprices_an_already_opened_but_unpaid_month()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var teacher = await CreateTeacherAsync(admin, "Yeniden", piano);
        var student = await CreateStudentAsync(admin, "Yeniden");
        var enrollment = await EnrollAsync(admin, student.Id, teacher.Id, piano);

        // Ay normal tarifeyle açılmış (6.000), henüz ödenmemiş.
        var opened = await ReadAsync<Receivables.ReceivableResponse>(await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2027-03")));
        Assert.Equal(6000m, opened.Amount);

        await ReadAsync<PrepayPlans.CreateResponse>(await admin.PostAsJsonAsync(
            $"/api/enrollments/{enrollment.Id}/prepay-plans",
            new PrepayPlans.CreateRequest(
                "2027-03", 4, new DateOnly(2027, 3, 1), PaymentMethod.Cash, null, null, ExpectedTotal: null)));

        // 4 ay kademesi -> %5. Aynı satır yeni tutarla güncellendi, ikinci bir satır açılmadı.
        var reprice = await db.Receivables.AsNoTracking()
            .SingleAsync(receivable => receivable.EnrollmentId == enrollment.Id && receivable.Period == "2027-03");
        Assert.Equal(opened.Id, reprice.Id);
        Assert.Equal(5700m, reprice.Amount);
        Assert.Equal(ReceivableStatus.Paid, reprice.Status);
    }

    [Fact]
    public async Task Prepay_plan_refuses_a_range_that_contains_an_already_paid_month()
    {
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var teacher = await CreateTeacherAsync(admin, "Cakisma", piano);
        var student = await CreateStudentAsync(admin, "Cakisma");
        var enrollment = await EnrollAsync(admin, student.Id, teacher.Id, piano);

        var paid = await ReadAsync<Receivables.ReceivableResponse>(await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2027-06")));
        (await admin.PostAsJsonAsync($"/api/receivables/{paid.Id}/payments",
            new Payments.CreateRequest(paid.Amount, new DateOnly(2027, 6, 1), PaymentMethod.Cash, null, null)))
            .EnsureSuccessStatusCode();

        var preview = await ReadAsync<PrepayPlans.PreviewResponse>(await admin.GetAsync(
            $"/api/enrollments/{enrollment.Id}/prepay-preview?startPeriod=2027-06&months=4"));
        Assert.NotEmpty(preview.Blockers);

        var response = await admin.PostAsJsonAsync(
            $"/api/enrollments/{enrollment.Id}/prepay-plans",
            new PrepayPlans.CreateRequest(
                "2027-06", 4, new DateOnly(2027, 6, 1), PaymentMethod.Cash, null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Prepay_plan_stops_when_the_screen_total_is_stale()
    {
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var teacher = await CreateTeacherAsync(admin, "Bayat", piano);
        var student = await CreateStudentAsync(admin, "Bayat");
        var enrollment = await EnrollAsync(admin, student.Id, teacher.Id, piano);

        var response = await admin.PostAsJsonAsync(
            $"/api/enrollments/{enrollment.Id}/prepay-plans",
            new PrepayPlans.CreateRequest(
                "2027-09", 4, new DateOnly(2027, 9, 1), PaymentMethod.Cash, null, null, ExpectedTotal: 1m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- Tarife değişikliği -------------------------------------------------------

    [Fact]
    public async Task A_new_tariff_closes_the_previous_one_and_leaves_written_dues_untouched()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var teacher = await CreateTeacherAsync(admin, "Zam", piano);
        var student = await CreateStudentAsync(admin, "Zam");
        var enrollment = await EnrollAsync(admin, student.Id, teacher.Id, piano);

        var before = await ReadAsync<Receivables.ReceivableResponse>(await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2026-12")));
        Assert.Equal(6000m, before.Amount);

        // Zam: 2028-09'dan itibaren geçerli yeni bir satır. Aradaki dönemlere dokunmaz,
        // böylece bu sınıftaki diğer testlerin fiyatları değişmez.
        var raised = await ReadAsync<TuitionRates.TuitionRateResponse>(await admin.PostAsJsonAsync(
            "/api/tuition-rates",
            new TuitionRates.CreateRequest(CourseKind.Individual, 4, 7500m, new DateOnly(2028, 9, 1), "TRY")));
        Assert.Equal(7500m, raised.MonthlyAmount);

        // Öncekisi bir gün önceden kapanmış olmalı - hiçbir gün iki tarifeye düşmez.
        var previous = await db.TuitionRates.AsNoTracking()
            .Where(rate => rate.CourseKind == CourseKind.Individual && rate.Id != raised.Id)
            .OrderByDescending(rate => rate.EffectiveFrom)
            .FirstAsync();
        Assert.Equal(new DateOnly(2028, 8, 31), previous.EffectiveUntil);

        // Yazılmış aidat değişmedi (fiyat snapshot'ı - docs/10-decisions.md A1).
        Assert.Equal(6000m, (await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == before.Id)).Amount);

        // Yeni dönem yeni fiyatı yansıtır.
        var after = await ReadAsync<Receivables.ReceivableResponse>(await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2028-09")));
        Assert.Equal(7500m, after.Amount);

        // Geçmişe doğru bir tarife açmak reddedilir.
        var backwards = await admin.PostAsJsonAsync(
            "/api/tuition-rates",
            new TuitionRates.CreateRequest(CourseKind.Individual, 4, 9000m, new DateOnly(2028, 8, 1), "TRY"));
        Assert.Equal(HttpStatusCode.Conflict, backwards.StatusCode);
    }

    // --- İndirim politikası -------------------------------------------------------

    [Fact]
    public async Task Billing_policy_round_trips_and_rejects_duplicate_tiers()
    {
        var admin = await CreateAdminClientAsync();

        var current = await ReadAsync<BillingPolicy.PolicyResponse>(await admin.GetAsync("/api/billing-policy"));
        Assert.Equal(5m, current.MultiCourseDiscountPercent);
        Assert.Equal(5m, current.SiblingDiscountPercent);
        Assert.Equal([4, 10], current.PrepayTiers.Select(tier => tier.MinMonths));

        var duplicate = await admin.PutAsJsonAsync("/api/billing-policy", new BillingPolicy.UpdateRequest(
            5m, 5m, 1, [new BillingPolicy.TierRequest(4, 5m), new BillingPolicy.TierRequest(4, 8m)]));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var invalidDueDay = await admin.PutAsJsonAsync("/api/billing-policy", new BillingPolicy.UpdateRequest(
            5m, 5m, 31, [new BillingPolicy.TierRequest(4, 5m)]));
        Assert.Equal(HttpStatusCode.BadRequest, invalidDueDay.StatusCode);

        // Kaydedilen politika aynen geri okunmalı; sonra başlangıç değerlerine döndürülür
        // (bu sınıftaki diğer testler seed edilmiş politikaya güveniyor).
        var saved = await ReadAsync<BillingPolicy.PolicyResponse>(await admin.PutAsJsonAsync(
            "/api/billing-policy", new BillingPolicy.UpdateRequest(
                7m, 6m, 10, [new BillingPolicy.TierRequest(3, 4m), new BillingPolicy.TierRequest(12, 15m)])));
        Assert.Equal(7m, saved.MultiCourseDiscountPercent);
        Assert.Equal(10, saved.DueDayOfMonth);
        Assert.Equal([3, 12], saved.PrepayTiers.Select(tier => tier.MinMonths));

        var restored = await ReadAsync<BillingPolicy.PolicyResponse>(await admin.PutAsJsonAsync(
            "/api/billing-policy", new BillingPolicy.UpdateRequest(
                5m, 5m, 1, [new BillingPolicy.TierRequest(4, 5m), new BillingPolicy.TierRequest(10, 10m)])));
        Assert.Equal([4, 10], restored.PrepayTiers.Select(tier => tier.MinMonths));
    }

    [Fact]
    public async Task Manual_discount_on_a_course_overrides_the_automatic_rules()
    {
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var art = await InstrumentIdAsync(admin, "ART");
        var teacher = await CreateTeacherAsync(admin, "Elle", piano, art);
        var student = await CreateStudentAsync(admin, "Elle");
        var enrollment = await EnrollAsync(admin, student.Id, teacher.Id, piano);
        await EnrollAsync(admin, student.Id, teacher.Id, art, CourseKind.Group); // otomatik %5

        var updated = await ReadAsync<Enrollments.EnrollmentResponse>(await admin.PatchAsJsonAsync(
            $"/api/students/{student.Id}/enrollments/{enrollment.Id}",
            new Enrollments.UpdateRequest(null, 20m, "Burslu öğrenci")));
        Assert.Equal(20m, updated.ManualDiscountPercent);

        var receivable = await ReadAsync<Receivables.ReceivableResponse>(await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, "2027-11")));

        Assert.Equal(20m, receivable.DiscountPercent);
        Assert.Equal(4800m, receivable.Amount);
        Assert.Equal("Burslu öğrenci (%20)", receivable.DiscountReason);
    }
}
