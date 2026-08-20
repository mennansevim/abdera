using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// docs/06-whatsapp.md sequence diyagramlari + docs/10-decisions.md A4/A5/A7/A8. Bu dosya
// gerçek Postgres gerektiren tarafı kapsar: UNIQUE(type,reference_type,reference_id)
// idempotency, FOR UPDATE SKIP LOCKED üzerinden gerçek dispatch, webhook idempotency/imza.
// Sessiz saat (A6) hesaplaması saf birim testinde (MessagingDomainTests) kapsanıyor -
// IClock burada gerçek SystemClock olduğu için "şu an sessiz saat içinde mi" deterministik
// biçimde kurulamıyor; bu bilinçli bir sınır, docs/11-progress-log.md'de not düşüldü.
public class MessagingFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public MessagingFlowTests(AbderaWebApplicationFactory factory)
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

    private record SeededLesson(Guid LessonId, Guid StudentId, Guid GuardianId, string GuardianPhone, Guid EnrollmentId);

    private static async Task<SeededLesson> SeedLessonAsync(HttpClient admin, string suffix)
    {
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest($"Öğretmen{suffix}", "Soyad", [piano.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;

        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest($"Öğrenci{suffix}", "Soyad", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        var phoneDigits = (Math.Abs(suffix.GetHashCode()) % 10_000_000).ToString("D7");
        var guardianPhone = $"0555{phoneDigits}";
        var guardian = (await (await admin.PostAsJsonAsync("/api/guardians",
                new Guardians.CreateRequest($"Veli{suffix}", "Soyad", guardianPhone)))
            .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;

        await admin.PostAsJsonAsync($"/api/students/{student.Id}/guardians",
            new LinkGuardianToStudent.Request(guardian.Id, "anne", true));

        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var dayOfWeek = suffix.GetHashCode() % 2 == 0 ? DayOfWeek.Monday : DayOfWeek.Wednesday;
        var seriesResponse = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, dayOfWeek, new TimeOnly(17, 0), 45, DateOnly.FromDateTime(DateTime.UtcNow), null));
        var created = (await seriesResponse.Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;
        Assert.True(created.Generation.Created > 0);

        var lessonsResponse = await admin.GetAsync(
            $"/api/calendar?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(90).ToString("O"))}&teacherId={teacher.Id}");
        var lessons = await lessonsResponse.Content.ReadFromJsonAsync<List<Calendar.LessonResponse>>(TestJson.Options);
        var lesson = lessons!.OrderBy(l => l.StartAt).First();

        return new SeededLesson(lesson.Id, student.Id, guardian.Id, guardianPhone, enrollment.Id);
    }

    [Fact]
    public async Task Creating_lesson_series_schedules_one_lesson_reminder_per_generated_lesson()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "sched1");

        var generatedLessonIds = await db.Lessons.Where(l => l.StudentId == seeded.StudentId).Select(l => l.Id).ToListAsync();
        var jobs = await db.NotificationJobs
            .Where(j => j.Type == NotificationJobType.LessonReminder && generatedLessonIds.Contains(j.ReferenceId))
            .ToListAsync();

        Assert.Equal(generatedLessonIds.Count, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(NotificationJobStatus.Pending, j.Status));
        Assert.All(jobs, j => Assert.Equal("+90" + seeded.GuardianPhone[1..], j.RecipientPhoneNumber));
    }

    [Fact]
    public async Task Regenerating_lesson_series_does_not_duplicate_reminder_jobs()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "sched2");

        var seriesId = await db.Lessons.Where(l => l.Id == seeded.LessonId).Select(l => l.LessonSeriesId).SingleAsync();
        var beforeCount = await db.NotificationJobs.CountAsync(j => j.Type == NotificationJobType.LessonReminder);

        // docs/10-decisions.md A5: aynı referans için ikinci kez job açılmaz (UNIQUE kısıt +
        // NotificationScheduler'ın kendi AnyAsync kontrolü).
        var regenerateResponse = await admin.PostAsJsonAsync($"/api/lesson-series/{seriesId}/generate", new { });
        Assert.Equal(HttpStatusCode.OK, regenerateResponse.StatusCode);

        var afterCount = await db.NotificationJobs.CountAsync(j => j.Type == NotificationJobType.LessonReminder);
        Assert.Equal(beforeCount, afterCount);
    }

    [Fact]
    public async Task Guardian_without_notification_consent_never_gets_a_job()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var guitar = instruments!.Single(i => i.Code == "GUITAR");

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("ConsentTeacher", "Soyad", [guitar.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("ConsentStudent", "Soyad", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var guardian = (await (await admin.PostAsJsonAsync("/api/guardians",
                new Guardians.CreateRequest("ConsentGuardian", "Soyad", "05559990011")))
            .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;
        await admin.PostAsJsonAsync($"/api/students/{student.Id}/guardians",
            new LinkGuardianToStudent.Request(guardian.Id, "anne", true));

        // Rızayı kapat - bugün itibarıyla bunu yapmanın tek yolu opt-out akışı (A8), admin
        // panelinde elle bir "rızayı kapat" uç noktası yok (bilinçli - opt-out yalnızca veli
        // kendi isteğiyle "dur" yazınca tetiklenir). Test burada invariant'ı doğrudan kuruyor.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE guardians SET notification_consent = false WHERE id = {guardian.Id}");

        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, guitar.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var seriesResponse = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Friday, new TimeOnly(16, 0), 45, DateOnly.FromDateTime(DateTime.UtcNow), null));
        var created = (await seriesResponse.Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;
        Assert.True(created.Generation.Created > 0);

        var jobCount = await db.NotificationJobs.CountAsync(j => j.RecipientPhoneNumber == "+905559990011");
        Assert.Equal(0, jobCount);
    }

    [Fact]
    public async Task Cancelling_a_lesson_cancels_its_pending_reminder()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "cxlrem");

        var jobBefore = await db.NotificationJobs.SingleAsync(j =>
            j.Type == NotificationJobType.LessonReminder && j.ReferenceId == seeded.LessonId);
        Assert.Equal(NotificationJobStatus.Pending, jobBefore.Status);

        var cancelResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/cancel",
            new CancelLesson.Request(CancelLesson.CancelledBy.Guardian, "hastalık"));
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var jobAfter = await db.NotificationJobs.AsNoTracking().SingleAsync(j => j.Id == jobBefore.Id);
        Assert.Equal(NotificationJobStatus.Cancelled, jobAfter.Status);
    }

    [Fact]
    public async Task Approving_a_change_request_cancels_old_reminder_and_schedules_new_reminder_and_rescheduled_notice()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "chgrem");

        var originalLesson = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == seeded.LessonId);
        var oldJob = await db.NotificationJobs.SingleAsync(j =>
            j.Type == NotificationJobType.LessonReminder && j.ReferenceId == seeded.LessonId);

        var proposedStart = originalLesson.StartAt.AddDays(1);
        var changeResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/change-requests",
            new ChangeRequests.CreateRequest("saat çakışması", proposedStart, proposedStart.AddMinutes(45)));
        Assert.Equal(HttpStatusCode.Created, changeResponse.StatusCode);
        var changeRequest = (await changeResponse.Content.ReadFromJsonAsync<ChangeRequests.ChangeRequestResponse>(TestJson.Options))!;

        var approveResponse = await admin.PostAsJsonAsync($"/api/change-requests/{changeRequest.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = (await approveResponse.Content.ReadFromJsonAsync<ChangeRequests.ApproveResponse>(TestJson.Options))!;

        var oldJobAfter = await db.NotificationJobs.AsNoTracking().SingleAsync(j => j.Id == oldJob.Id);
        Assert.Equal(NotificationJobStatus.Cancelled, oldJobAfter.Status);

        var newReminder = await db.NotificationJobs.SingleOrDefaultAsync(j =>
            j.Type == NotificationJobType.LessonReminder && j.ReferenceId == approved.NewLessonId);
        Assert.NotNull(newReminder);
        Assert.Equal(NotificationJobStatus.Pending, newReminder!.Status);

        var rescheduledNotice = await db.NotificationJobs.SingleOrDefaultAsync(j =>
            j.Type == NotificationJobType.LessonRescheduled && j.ReferenceId == approved.NewLessonId);
        Assert.NotNull(rescheduledNotice);
    }

    [Fact]
    public async Task Dispatcher_sends_a_due_job_through_fake_client_and_marks_it_sent()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "dispatch1");

        var job = await db.NotificationJobs.SingleAsync(j =>
            j.Type == NotificationJobType.LessonReminder && j.ReferenceId == seeded.LessonId);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE notification_jobs SET scheduled_at = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE id = {job.Id}");

        // NotificationDispatcher (Notifications:DispatchIntervalSeconds=1 test override'ı)
        // FOR UPDATE SKIP LOCKED ile bu job'ı en geç birkaç saniye içinde işlemeli.
        NotificationJob? sentJob = null;
        for (var i = 0; i < 30 && sentJob is null; i++)
        {
            await Task.Delay(500);
            var current = await db.NotificationJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
            if (current.Status is NotificationJobStatus.Sent or NotificationJobStatus.Failed)
            {
                sentJob = current;
            }
        }

        Assert.NotNull(sentJob);
        Assert.Equal(NotificationJobStatus.Sent, sentJob!.Status);
        Assert.NotNull(sentJob.SentAt);

        var outbound = await db.WhatsAppMessages.AsNoTracking()
            .SingleAsync(m => m.NotificationJobId == job.Id);
        Assert.Contains("Ders Hatırlatması", outbound.BodySnapshot);
    }

    [Fact]
    public async Task Rsvp_button_webhook_records_attending_response()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "rsvpwh");

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/whatsapp/simulate-rsvp", new
        {
            fromPhoneNumber = seeded.GuardianPhone,
            action = RsvpButtonPayload.AttendingAction,
            lessonId = seeded.LessonId,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rsvp = await db.LessonRsvps.SingleAsync(r => r.LessonId == seeded.LessonId && r.GuardianId == seeded.GuardianId);
        Assert.Equal(RsvpResponse.Attending, rsvp.Response);
        Assert.Equal(RsvpSource.WhatsApp, rsvp.Source);
    }

    [Theory]
    [InlineData("ders", "sonraki")]
    [InlineData("okula yaz", "yönetimine iletildi")]
    public async Task Deterministic_intent_reply_is_sent_as_outbound_free_text(string incomingText, string expectedSubstring)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "intent" + incomingText.GetHashCode());

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/whatsapp/simulate-text", new
        {
            fromPhoneNumber = seeded.GuardianPhone,
            body = incomingText,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var outbound = await db.WhatsAppMessages.AsNoTracking()
            .Where(m => m.GuardianId == seeded.GuardianId && m.Direction == MessageDirection.Outbound)
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync();
        Assert.Contains(expectedSubstring, outbound.BodySnapshot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Opt_out_disables_consent_cancels_pending_jobs_and_sends_single_confirmation()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "optout");
        var normalizedPhone = "+90" + seeded.GuardianPhone[1..];

        var pendingBefore = await db.NotificationJobs.CountAsync(j =>
            j.RecipientPhoneNumber == normalizedPhone && j.Status == NotificationJobStatus.Pending);
        Assert.True(pendingBefore > 0);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/whatsapp/simulate-text", new
        {
            fromPhoneNumber = seeded.GuardianPhone,
            body = "dur",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var guardian = await db.Guardians.AsNoTracking().SingleAsync(g => g.Id == seeded.GuardianId);
        Assert.False(guardian.NotificationConsent);

        var pendingAfter = await db.NotificationJobs.CountAsync(j =>
            j.RecipientPhoneNumber == normalizedPhone && j.Status == NotificationJobStatus.Pending);
        Assert.Equal(0, pendingAfter);

        var confirmations = await db.WhatsAppMessages.AsNoTracking()
            .Where(m => m.GuardianId == seeded.GuardianId && m.Direction == MessageDirection.Outbound
                        && m.BodySnapshot.Contains("durduruldu"))
            .CountAsync();
        Assert.Equal(1, confirmations);
    }

    [Fact]
    public async Task Webhook_rejects_request_with_invalid_signature()
    {
        var client = _factory.CreateClient();
        const string body = """{"entry":[]}""";

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Hub-Signature-256", "sha256=0000000000000000000000000000000000000000000000000000000000000000");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_does_not_reprocess_a_duplicate_provider_event_id()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "webhookidem");

        var messageId = $"wamid.idem-test-{Guid.NewGuid():N}";
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            entry = new[]
            {
                new
                {
                    changes = new[]
                    {
                        new
                        {
                            value = new
                            {
                                messages = new[]
                                {
                                    new
                                    {
                                        id = messageId,
                                        from = "90" + seeded.GuardianPhone[1..],
                                        type = "text",
                                        text = new { body = "aidat" },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        });

        // appsettings.json/test override'ında WhatsApp:AppSecret tanımlı değil -> Webhooks.cs
        // boş string'e düşer; burada da aynı boş anahtarla imzalıyoruz.
        var signature = "sha256=" + Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(""), Encoding.UTF8.GetBytes(body)));

        async Task<HttpResponseMessage> PostOnceAsync()
        {
            var client = _factory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Hub-Signature-256", signature);
            return await client.SendAsync(request);
        }

        var first = await PostOnceAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await PostOnceAsync();
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var eventCount = await db.WhatsAppWebhookEvents.CountAsync(e => e.ProviderEventId == messageId);
        Assert.Equal(1, eventCount);

        var inboundCount = await db.WhatsAppMessages.CountAsync(m => m.GuardianId == seeded.GuardianId && m.BodySnapshot == "aidat");
        Assert.Equal(1, inboundCount);
    }
}
