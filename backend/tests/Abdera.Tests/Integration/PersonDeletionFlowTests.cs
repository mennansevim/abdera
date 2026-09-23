using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.People.Infrastructure;
using Abdera.Api.Modules.Scheduling.Features;
using Abdera.Api.Modules.Show.Domain;
using Abdera.Api.Modules.Show.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Kalıcı silme (kullanıcı isteği: "tamamen silme opsiyonu olmalı").
//
// Bu dosyanın en önemli testi YETİM SATIR testidir: bir öğrenci silindikten sonra ona,
// kayıtlarına, derslerine veya aidatlarına referans veren HİÇBİR satır kalmamalı. Kişiye
// referans veren tabloların çoğunda veritabanı FK'sı yok (modüller arası bağlar açık id
// sorgularıyla kuruluyor), dolayısıyla eksiksizliği veritabanı değil bu test garanti eder -
// yeni bir tablo eklendiğinde burası kırılır ve PersonEraser güncellenir.
public class PersonDeletionFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public PersonDeletionFlowTests(AbderaWebApplicationFactory factory)
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

    private static Task<HttpResponseMessage> PostPaymentAsync(
        HttpClient client, Guid receivableId, Payments.CreateRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/receivables/{receivableId}/payments")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return client.SendAsync(message);
    }

    private record Seeded(Guid StudentId, Guid TeacherId, Guid EnrollmentId, Guid GuardianId, Guid ReceivableId, Guid ShowId);

    // Bir öğrenciyi mümkün olduğunca "dolu" kurar: veli, ders serisi ve üretilmiş dersler,
    // yoklama, aidat, ödeme, telafi kredisi, fotoğraf ve gösteri programındaki sırası.
    private async Task<Seeded> SeedLoadedStudentAsync(HttpClient admin, string suffix)
    {
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(instrument => instrument.Code == "PIANO");

        var teacher = (await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/teachers", new Teachers.CreateRequest($"Sil{suffix}", "Ogretmen", [piano.Id], $"sil.{suffix}@test.local")))).Teacher;

        var student = await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync(
            "/api/students", new Students.CreateRequest($"Sil{suffix}", "Ogrenci", new DateOnly(2014, 1, 1))));

        var phoneDigits = (Math.Abs(suffix.GetHashCode()) % 10_000_000).ToString("D7");
        var guardian = await ReadAsync<Guardians.GuardianResponse>(await admin.PostAsJsonAsync(
            "/api/guardians", new Guardians.CreateRequest($"Veli{suffix}", "Soyad", $"0533{phoneDigits}")));
        (await admin.PostAsJsonAsync($"/api/students/{student.Id}/guardians",
            new LinkGuardianToStudent.Request(guardian.Id, "anne", true))).EnsureSuccessStatusCode();

        var enrollment = await ReadAsync<Enrollments.EnrollmentResponse>(await admin.PostAsJsonAsync(
            $"/api/students/{student.Id}/enrollments",
            new Enrollments.CreateRequest(teacher.Id, piano.Id, new DateOnly(2026, 9, 1), CourseKind.Individual)));

        (await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Tuesday, new TimeOnly(18, 0), 45, new DateOnly(2026, 9, 1), null)))
            .EnsureSuccessStatusCode();

        // Kayıt açılışı bu ayın aidatını zaten açtı (EnrollmentReceivableOpener); silme akışı
        // tam olarak bu otomatik satırı (ve üstündeki ödemeyi) temizleyebilmeli.
        var period = TestPeriods.Current(_factory.Services);
        var billing = await ReadAsync<List<StudentBilling.StudentBillingResponse>>(
            await admin.GetAsync($"/api/students/{student.Id}/billing"));
        var receivable = billing.Single(row => row.EnrollmentId == enrollment.Id).Receivables.Single(r => r.Period == period);
        (await PostPaymentAsync(admin, receivable.Id,
            new Payments.CreateRequest(1000m, new DateOnly(2026, 9, 3), PaymentMethod.Cash, null, null)))
            .EnsureSuccessStatusCode();

        var show = await ReadAsync<Shows.ShowDetailResponse>(await admin.PostAsJsonAsync(
            "/api/shows", new Shows.CreateRequest($"Silme {suffix}", null, new DateTimeOffset(2027, 6, 1, 16, 0, 0, TimeSpan.Zero))));
        (await admin.PostAsJsonAsync($"/api/shows/{show.Id}/items", new Shows.ItemRequest(
            ShowItemKind.Performance, "1. Bölüm", student.Id, piano.Id, teacher.Id, "Eser", "Besteci", 4, null)))
            .EnsureSuccessStatusCode();

        return new Seeded(student.Id, teacher.Id, enrollment.Id, guardian.Id, receivable.Id, show.Id);
    }

    [Fact]
    public async Task Impact_report_counts_everything_that_will_be_removed()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLoadedStudentAsync(admin, "etki");

        var impact = await ReadAsync<PersonEraser.StudentImpact>(
            await admin.GetAsync($"/api/students/{seeded.StudentId}/deletion-impact"));

        Assert.Equal(1, impact.Enrollments);
        Assert.True(impact.Lessons > 0, "Ders serisinden ders üretilmiş olmalı.");
        Assert.Equal(1, impact.Receivables);
        Assert.Equal(1, impact.Payments);
        Assert.Equal(1000m, impact.CollectedAmount);
        Assert.Equal(1, impact.ShowItems);
        // Velinin başka çocuğu yok - silindikten sonra sahipsiz kalacağı bildirilmeli.
        Assert.Equal(1, impact.GuardiansLeftWithoutStudents);
    }

    [Fact]
    public async Task Deleting_a_student_with_payments_needs_an_explicit_confirmation()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLoadedStudentAsync(admin, "onay");

        var blocked = await admin.DeleteAsync($"/api/students/{seeded.StudentId}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("ödeme", await blocked.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        await using var db = await _factory.CreateDbContextAsync();
        Assert.True(await db.Students.AnyAsync(student => student.Id == seeded.StudentId));
    }

    [Fact]
    public async Task Deleting_a_student_leaves_no_orphan_row_anywhere()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLoadedStudentAsync(admin, "yetim");

        await using var db = await _factory.CreateDbContextAsync();
        var enrollmentIds = await db.Enrollments.Where(e => e.StudentId == seeded.StudentId).Select(e => e.Id).ToListAsync();
        var seriesIds = await db.LessonSeries.Where(s => enrollmentIds.Contains(s.EnrollmentId)).Select(s => s.Id).ToListAsync();
        var lessonIds = await db.Lessons.Where(l => l.StudentId == seeded.StudentId).Select(l => l.Id).ToListAsync();
        var receivableIds = await db.Receivables.Where(r => enrollmentIds.Contains(r.EnrollmentId)).Select(r => r.Id).ToListAsync();
        var paymentIds = await db.Payments.Where(p => receivableIds.Contains(p.ReceivableId)).Select(p => p.Id).ToListAsync();
        Assert.NotEmpty(lessonIds);
        Assert.NotEmpty(paymentIds);

        var deleted = await admin.DeleteAsync($"/api/students/{seeded.StudentId}?force=true");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        db.ChangeTracker.Clear();
        Assert.False(await db.Students.AnyAsync(s => s.Id == seeded.StudentId));
        Assert.False(await db.Enrollments.AnyAsync(e => enrollmentIds.Contains(e.Id)));
        Assert.False(await db.LessonSeries.AnyAsync(s => seriesIds.Contains(s.Id)));
        Assert.False(await db.Lessons.AnyAsync(l => lessonIds.Contains(l.Id)));
        Assert.False(await db.LessonAttendances.AnyAsync(a => lessonIds.Contains(a.LessonId)));
        Assert.False(await db.LessonRsvps.AnyAsync(r => lessonIds.Contains(r.LessonId)));
        Assert.False(await db.LessonNotes.AnyAsync(n => lessonIds.Contains(n.LessonId)));
        Assert.False(await db.LessonChangeRequests.AnyAsync(c => lessonIds.Contains(c.LessonId)));
        Assert.False(await db.PracticeAssignments.AnyAsync(p => lessonIds.Contains(p.LessonId)));
        Assert.False(await db.SkillAssessments.AnyAsync(a => a.StudentId == seeded.StudentId));
        Assert.False(await db.PracticeJournalEntries.AnyAsync(p => p.StudentId == seeded.StudentId));
        Assert.False(await db.Receivables.AnyAsync(r => receivableIds.Contains(r.Id)));
        Assert.False(await db.Payments.AnyAsync(p => paymentIds.Contains(p.Id)));
        Assert.False(await db.PaymentCorrections.AnyAsync(c => paymentIds.Contains(c.PaymentId)));
        Assert.False(await db.MakeupCredits.AnyAsync(c => c.StudentId == seeded.StudentId));
        Assert.False(await db.StudentGuardians.AnyAsync(sg => sg.StudentId == seeded.StudentId));
        Assert.False(await db.StudentPhotos.AnyAsync(p => p.StudentId == seeded.StudentId));
        Assert.False(await db.ShowItems.AnyAsync(i => i.StudentId == seeded.StudentId));
        Assert.False(await db.NotificationJobs.AnyAsync(j => lessonIds.Contains(j.ReferenceId) || receivableIds.Contains(j.ReferenceId)));
        Assert.False(await db.StaffNotifications.AnyAsync(n => lessonIds.Contains(n.ReferenceId) || receivableIds.Contains(n.ReferenceId)));

        // Veli KALIR - kişisel veriyi kullanıcının haberi olmadan silmiyoruz, yalnızca
        // etkisini bildiriyoruz.
        Assert.True(await db.Guardians.AnyAsync(g => g.Id == seeded.GuardianId));

        // Silme işleminin kendisi audit'te durur.
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "student.deleted" && log.EntityId == seeded.StudentId));
    }

    [Fact]
    public async Task Deleting_a_teacher_with_reassignment_keeps_every_student_record()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLoadedStudentAsync(admin, "devir");

        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(instrument => instrument.Code == "PIANO");
        var successor = (await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/teachers", new Teachers.CreateRequest("Devralan", "Ogretmen", [piano.Id], "devralan@test.local")))).Teacher;

        await using var db = await _factory.CreateDbContextAsync();
        var lessonCountBefore = await db.Lessons.CountAsync(l => l.TeacherId == seeded.TeacherId);
        Assert.True(lessonCountBefore > 0);

        var response = await admin.DeleteAsync($"/api/teachers/{seeded.TeacherId}?reassignTo={successor.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        db.ChangeTracker.Clear();
        Assert.False(await db.Teachers.AnyAsync(t => t.Id == seeded.TeacherId));
        // Öğrenci, kaydı, dersleri ve ödemesi olduğu gibi duruyor - yalnızca öğretmeni değişti.
        Assert.True(await db.Students.AnyAsync(s => s.Id == seeded.StudentId));
        Assert.True(await db.Receivables.AnyAsync(r => r.Id == seeded.ReceivableId));
        Assert.True(await db.Payments.AnyAsync(p => p.ReceivableId == seeded.ReceivableId));
        Assert.Equal(successor.Id, (await db.Enrollments.SingleAsync(e => e.Id == seeded.EnrollmentId)).TeacherId);
        Assert.Equal(lessonCountBefore, await db.Lessons.CountAsync(l => l.TeacherId == successor.Id));
        Assert.False(await db.Lessons.AnyAsync(l => l.TeacherId == seeded.TeacherId));
    }

    [Fact]
    public async Task Deleting_a_teacher_without_reassignment_warns_then_removes_the_login_account()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLoadedStudentAsync(admin, "tumden");

        await using var db = await _factory.CreateDbContextAsync();
        var userId = (await db.Teachers.AsNoTracking().SingleAsync(t => t.Id == seeded.TeacherId)).UserId;
        Assert.NotNull(userId);

        var blocked = await admin.DeleteAsync($"/api/teachers/{seeded.TeacherId}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("devred", await blocked.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var forced = await admin.DeleteAsync($"/api/teachers/{seeded.TeacherId}?force=true");
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);

        db.ChangeTracker.Clear();
        Assert.False(await db.Teachers.AnyAsync(t => t.Id == seeded.TeacherId));
        Assert.False(await db.Users.AnyAsync(u => u.Id == userId));
        Assert.False(await db.TeacherInstruments.AnyAsync(ti => ti.TeacherId == seeded.TeacherId));
        Assert.False(await db.Enrollments.AnyAsync(e => e.TeacherId == seeded.TeacherId));
        Assert.False(await db.Receivables.AnyAsync(r => r.Id == seeded.ReceivableId));
        // Öğrencinin kendisi durur - silinen öğretmendi.
        Assert.True(await db.Students.AnyAsync(s => s.Id == seeded.StudentId));
    }

    [Fact]
    public async Task Reassignment_target_must_be_another_active_teacher()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLoadedStudentAsync(admin, "hedef");

        var toSelf = await admin.DeleteAsync($"/api/teachers/{seeded.TeacherId}?reassignTo={seeded.TeacherId}");
        Assert.Equal(HttpStatusCode.BadRequest, toSelf.StatusCode);

        var toMissing = await admin.DeleteAsync($"/api/teachers/{seeded.TeacherId}?reassignTo={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, toMissing.StatusCode);
    }
}
