using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Progress.Features;
using Abdera.Api.Modules.Progress.Infrastructure;
using Abdera.Api.Modules.Scheduling.Features;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Tests.Integration;

// Ses Defteri (issue #52) gerçek cookie auth + multipart yükleme + disk + PostgreSQL zinciriyle.
// Güvenlik sınırları: rızasız kayıt yok, başka öğretmenin öğrencisi 403, paylaşılmamış kayıt
// veliye görünmez, rıza geri alınınca kayıtlar diskten de silinir. Yer sınırları: ses
// veritabanına girmez, dönem kotası işler, gece bakımı budar/yetimi siler/artımlı yedekler.
public class AudioClipFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public AudioClipFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Teacher_records_with_consent_shares_with_guardian_and_withdrawn_consent_erases_files()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedAsync(admin, "ses-mutlu");
        using var teacher = await LoginAsync(seeded.TeacherEmail, seeded.TeacherPassword);

        // Rıza yokken kayıt alınamaz.
        var noConsent = await UploadAsync(teacher, seeded.LessonId, FakeWebm(8_000), "Für Elise", 20);
        Assert.Equal(HttpStatusCode.Conflict, noConsent.StatusCode);
        await using (var db = await _factory.CreateDbContextAsync())
            Assert.False(await db.AudioClips.AnyAsync(item => item.StudentId == seeded.StudentId));

        (await admin.PutAsJsonAsync($"/api/students/{seeded.StudentId}/audio-consent", new AudioClips.ConsentRequest(true)))
            .EnsureSuccessStatusCode();

        // Yönetici kayıt almaz; dersin öğretmeni alır.
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(admin, seeded.LessonId, FakeWebm(8_000), null, 20)).StatusCode);
        // İçerik sahte ses dosyası değilse reddedilir (Content-Type başlığına güvenilmez).
        Assert.Equal(HttpStatusCode.BadRequest,
            (await UploadAsync(teacher, seeded.LessonId, "%PDF-1.7 not audio................"u8.ToArray().Concat(new byte[2000]).ToArray(), null, 20)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(teacher, seeded.LessonId, FakeWebm(AudioClip.MaxBytes + 1), null, 20)).StatusCode);

        var created = await UploadAsync(teacher, seeded.LessonId, FakeWebm(8_000), "Für Elise", 20);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var clip = (await created.Content.ReadFromJsonAsync<AudioClips.ClipResponse>(TestJson.Options))!;
        Assert.Equal("audio/webm", clip.ContentType);
        Assert.Equal(8_000, clip.SizeBytes);
        Assert.False(clip.SharedWithGuardian);

        // Ses dosyası diskte, veritabanında yalnızca meta veri.
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var row = await db.AudioClips.SingleAsync(item => item.Id == clip.Id);
            Assert.True(File.Exists(Path.Combine(_factory.AudioDirectory, row.FileName)));
        }

        var content = await teacher.GetAsync($"/api/audio-clips/{clip.Id}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("audio/webm", content.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", content.Headers.CacheControl?.ToString());
        Assert.Equal(8_000, (await content.Content.ReadAsByteArrayAsync()).Length);

        var list = (await (await admin.GetAsync($"/api/students/{seeded.StudentId}/audio-clips"))
            .Content.ReadFromJsonAsync<AudioClips.StudentClipsResponse>(TestJson.Options))!;
        Assert.True(list.Consent);
        Assert.Equal(1, list.TermUsed);
        Assert.StartsWith("Öğretmen", Assert.Single(list.Clips).TeacherName);

        // Başka öğretmen bu öğrencinin kaydına erişemez.
        var other = await SeedAsync(admin, "ses-baska");
        using var otherTeacher = await LoginAsync(other.TeacherEmail, other.TeacherPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTeacher.GetAsync($"/api/audio-clips/{clip.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTeacher.GetAsync($"/api/students/{seeded.StudentId}/audio-clips")).StatusCode);

        // Paylaşılmamış kayıt veliye görünmez, varlığı bile sızmaz.
        using var guardian = await LoginAsGuardianAsync(seeded.GuardianPhone);
        var guardianList = (await (await guardian.GetAsync($"/api/guardian/me/students/{seeded.StudentId}/audio-clips"))
            .Content.ReadFromJsonAsync<AudioClips.GuardianClipsResponse>(TestJson.Options))!;
        Assert.Empty(guardianList.Clips);
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/guardian/me/audio-clips/{clip.Id}/content")).StatusCode);

        (await teacher.PutAsJsonAsync($"/api/audio-clips/{clip.Id}/sharing", new AudioClips.ShareRequest(true))).EnsureSuccessStatusCode();
        guardianList = (await (await guardian.GetAsync($"/api/guardian/me/students/{seeded.StudentId}/audio-clips"))
            .Content.ReadFromJsonAsync<AudioClips.GuardianClipsResponse>(TestJson.Options))!;
        Assert.Equal("Für Elise", Assert.Single(guardianList.Clips).PieceTitle);
        Assert.Equal(HttpStatusCode.OK, (await guardian.GetAsync($"/api/guardian/me/audio-clips/{clip.Id}/content")).StatusCode);

        // Başka bir velinin öğrencisi: erişim yok.
        using var otherGuardian = await LoginAsGuardianAsync(other.GuardianPhone);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherGuardian.GetAsync($"/api/guardian/me/audio-clips/{clip.Id}/content")).StatusCode);

        // Veli rızayı geri alır: kayıt satırı ve dosyası gider, karar audit'te durur.
        var withdrawn = (await (await guardian.PutAsJsonAsync(
                $"/api/guardian/me/students/{seeded.StudentId}/audio-consent", new AudioClips.ConsentRequest(false)))
            .Content.ReadFromJsonAsync<AudioClips.ConsentResponse>(TestJson.Options))!;
        Assert.Equal(1, withdrawn.DeletedClips);
        await using (var db = await _factory.CreateDbContextAsync())
        {
            Assert.False(await db.AudioClips.AnyAsync(item => item.StudentId == seeded.StudentId));
            Assert.Empty(Directory.GetFiles(_factory.AudioDirectory, $"{clip.Id:N}*"));
            Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "audio_clip.shared" && log.EntityId == clip.Id));
            Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "student.audio_consent_changed" && log.EntityId == seeded.StudentId));
        }
        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(teacher, seeded.LessonId, FakeWebm(8_000), null, 20)).StatusCode);
    }

    [Fact]
    public async Task Term_quota_stops_new_recordings_until_an_old_one_is_deleted()
    {
        await using var limited = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["AudioClips:MaxPerStudentPerTerm"] = "1" })));
        var admin = await CreateAdminClientAsync(limited);
        var seeded = await SeedAsync(admin, "ses-kota");
        using var teacher = await LoginAsync(seeded.TeacherEmail, seeded.TeacherPassword, limited);
        (await admin.PutAsJsonAsync($"/api/students/{seeded.StudentId}/audio-consent", new AudioClips.ConsentRequest(true)))
            .EnsureSuccessStatusCode();

        var first = await UploadAsync(teacher, seeded.LessonId, FakeWebm(4_000), null, 10);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var full = await UploadAsync(teacher, seeded.LessonId, FakeWebm(4_000), null, 10);
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Contains("sınır", await full.Content.ReadAsStringAsync());

        var clip = (await first.Content.ReadFromJsonAsync<AudioClips.ClipResponse>(TestJson.Options))!;
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.DeleteAsync($"/api/audio-clips/{clip.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(teacher, seeded.LessonId, FakeWebm(4_000), null, 10)).StatusCode);
    }

    [Fact]
    public async Task Nightly_maintenance_prunes_old_takes_removes_orphans_and_backs_up_each_clip_once()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedAsync(admin, "ses-bakim");
        var store = _factory.Services.GetRequiredService<AudioClipStore>();
        var backup = _factory.Services.GetRequiredService<IBackupStorage>();
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        var clock = _factory.Services.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        await using var db = await _factory.CreateDbContextAsync();
        var student = await db.Students.SingleAsync(item => item.Id == seeded.StudentId);
        student.SetAudioRecordingConsent(true, now);
        AudioClip Add(DateTimeOffset at, bool starred = false)
        {
            var clip = AudioClip.Create(seeded.StudentId, seeded.TeacherId, seeded.LessonId, "Minuet", AudioFormat.WebM, 4_000, 10, Guid.NewGuid(), at);
            if (starred) clip.SetStarred(true, at);
            db.AudioClips.Add(clip);
            store.SaveAsync(clip.FileName, FakeWebm(4_000)).GetAwaiter().GetResult();
            return clip;
        }
        var first = Add(now.AddMonths(-20));
        var middle = Add(now.AddMonths(-19));
        var starred = Add(now.AddMonths(-18), starred: true);
        var last = Add(now.AddDays(-3));
        await db.SaveChangesAsync();

        // Satırı olmayan, bir saatten eski dosya yetimdir; yeni olan (yükleme sürüyor olabilir) kalır.
        var orphan = Path.Combine(store.Root, $"{Guid.NewGuid():N}.webm");
        await File.WriteAllBytesAsync(orphan, FakeWebm(2_000));
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddHours(-2));
        var fresh = Path.Combine(store.Root, $"{Guid.NewGuid():N}.webm");
        await File.WriteAllBytesAsync(fresh, FakeWebm(2_000));

        var result = await AudioClipMaintenance.RunAsync(db, store, backup, config, clock, CancellationToken.None);

        Assert.Equal(1, result.Pruned);
        Assert.True(result.OrphansDeleted >= 1);
        db.ChangeTracker.Clear();
        var remaining = await db.AudioClips.Where(item => item.StudentId == seeded.StudentId).ToListAsync();
        Assert.Equal(new HashSet<Guid> { first.Id, starred.Id, last.Id }, remaining.Select(item => item.Id).ToHashSet());
        Assert.False(File.Exists(Path.Combine(store.Root, middle.FileName)));
        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(fresh));
        Assert.All(remaining, item => Assert.NotNull(item.BackedUpAt));

        var uploaded = await backup.ListAsync(AudioClipMaintenance.RemoteDirectory);
        Assert.Contains(uploaded, file => file.Name == $"audio-clips/{last.FileName}.enc");
        // Ses yedeği veritabanı yedeklerinin tutulma temizliğine (kök klasör) karışmaz.
        Assert.DoesNotContain(await backup.ListAsync(), file => file.Name.StartsWith("audio-clips/"));

        // İkinci tur aynı kayıtları tekrar yüklemez.
        var second = await AudioClipMaintenance.RunAsync(db, store, backup, config, clock, CancellationToken.None);
        Assert.Equal(0, second.BackedUp);
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "audio_clip.pruned"));
        File.Delete(fresh);
    }

    private static byte[] FakeWebm(int size)
    {
        var bytes = new byte[size];
        new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }.CopyTo(bytes, 0);
        return bytes;
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid lessonId, byte[] bytes, string? pieceTitle, int durationSeconds)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        form.Add(file, "file", "kayit.webm");
        if (pieceTitle is not null) form.Add(new StringContent(pieceTitle), "pieceTitle");
        form.Add(new StringContent(durationSeconds.ToString()), "durationSeconds");
        return await client.PostAsync($"/api/lessons/{lessonId}/audio-clips", form);
    }

    private async Task<HttpClient> CreateAdminClientAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? _factory).CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> LoginAsync(string email, string password, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? _factory).CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new Login.Request(email, password))).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> LoginAsGuardianAsync(string rawPhone)
    {
        var client = _factory.CreateClient();
        var otp = (await (await client.PostAsJsonAsync("/api/guardian/otp/request", new GuardianAuth.RequestOtpRequest(rawPhone)))
            .Content.ReadFromJsonAsync<GuardianAuth.RequestOtpResponse>(TestJson.Options))!;
        (await client.PostAsJsonAsync("/api/guardian/otp/verify", new GuardianAuth.VerifyOtpRequest(rawPhone, otp.DebugCode!)))
            .EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<Seeded> SeedAsync(HttpClient admin, string suffix)
    {
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(instrument => instrument.Code == "PIANO");

        var teacherEmail = $"teacher-{suffix}@test.local";
        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest($"Öğretmen{suffix}", "Soyad", [piano.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest($"Öğrenci{suffix}", "Soyad", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        var rawPhone = $"0544{Math.Abs(suffix.GetHashCode()) % 10_000_000:D7}";
        var guardian = (await (await admin.PostAsJsonAsync("/api/guardians",
                new Guardians.CreateRequest($"Veli{suffix}", "Soyad", rawPhone)))
            .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;
        (await admin.PostAsJsonAsync($"/api/students/{student.Id}/guardians",
            new LinkGuardianToStudent.Request(guardian.Id, "anne", true))).EnsureSuccessStatusCode();

        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Teacher.Id, piano.Id, DateOnly.FromDateTime(DateTime.UtcNow))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
        (await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Thursday, new TimeOnly(16, 0), 45, DateOnly.FromDateTime(DateTime.UtcNow), null)))
            .EnsureSuccessStatusCode();

        var lessons = await (await admin.GetAsync(
                $"/api/calendar?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}" +
                $"&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(90).ToString("O"))}&teacherId={teacher.Teacher.Id}"))
            .Content.ReadFromJsonAsync<List<Calendar.LessonResponse>>(TestJson.Options);

        return new Seeded(
            lessons!.OrderBy(item => item.StartAt).First().Id, student.Id, teacher.Teacher.Id,
            $"Öğretmen{suffix} Soyad", teacherEmail, teacher.TemporaryPassword!, rawPhone);
    }

    private record Seeded(
        Guid LessonId, Guid StudentId, Guid TeacherId, string TeacherName, string TeacherEmail, string TeacherPassword,
        string GuardianPhone);
}
