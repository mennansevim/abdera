using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

public class MessagingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private static NotificationJob CreateJob() => NotificationJob.Create(
        NotificationJobType.LessonReminder, "+905551234567", "lesson", Guid.NewGuid(), Now.AddHours(1), Now);

    [Fact]
    public void NotificationJob_Claim_moves_pending_to_processing_and_increments_attempts()
    {
        var job = CreateJob();

        job.Claim(Now);

        Assert.Equal(NotificationJobStatus.Processing, job.Status);
        Assert.Equal(1, job.AttemptCount);
    }

    [Fact]
    public void NotificationJob_Claim_throws_when_not_pending()
    {
        var job = CreateJob();
        job.Claim(Now);

        Assert.Throws<ConflictException>(() => job.Claim(Now));
    }

    [Fact]
    public void NotificationJob_MarkFailed_returns_to_pending_while_under_max_attempts()
    {
        var job = CreateJob();
        job.Claim(Now); // attempt 1

        job.MarkFailed("hata", maxAttempts: 5, Now);

        Assert.Equal(NotificationJobStatus.Pending, job.Status);
        Assert.Equal("hata", job.LastError);
    }

    [Fact]
    public void NotificationJob_MarkFailed_moves_to_failed_when_max_attempts_reached()
    {
        var job = CreateJob();
        job.Claim(Now); // attempt 1

        job.MarkFailed("hata", maxAttempts: 1, Now);

        Assert.Equal(NotificationJobStatus.Failed, job.Status);
    }

    [Fact]
    public void NotificationJob_MarkSent_sets_sent_at_and_status()
    {
        var job = CreateJob();
        job.Claim(Now);

        job.MarkSent(Now);

        Assert.Equal(NotificationJobStatus.Sent, job.Status);
        Assert.Equal(Now, job.SentAt);
    }

    [Fact]
    public void NotificationJob_Cancel_is_noop_when_already_sent()
    {
        var job = CreateJob();
        job.Claim(Now);
        job.MarkSent(Now);

        job.Cancel(Now.AddMinutes(5));

        // docs/05-state-models.md: SENT -> CANCELLED gecisi yok, sessizce yok sayilir.
        Assert.Equal(NotificationJobStatus.Sent, job.Status);
    }

    [Fact]
    public void NotificationJob_Cancel_from_pending_moves_to_cancelled()
    {
        var job = CreateJob();

        job.Cancel(Now);

        Assert.Equal(NotificationJobStatus.Cancelled, job.Status);
    }

    [Fact]
    public void NotificationJob_RetryManually_throws_unless_failed()
    {
        var job = CreateJob();

        Assert.Throws<ConflictException>(() => job.RetryManually(Now));
    }

    [Fact]
    public void NotificationJob_RetryManually_moves_failed_to_pending()
    {
        var job = CreateJob();
        job.Claim(Now);
        job.MarkFailed("hata", maxAttempts: 1, Now); // -> Failed

        job.RetryManually(Now);

        Assert.Equal(NotificationJobStatus.Pending, job.Status);
    }

    [Theory]
    [InlineData(NotificationJobType.PaymentReminder, true)]
    [InlineData(NotificationJobType.Birthday, true)]
    [InlineData(NotificationJobType.PackageEnding, true)]
    [InlineData(NotificationJobType.LessonReminder, false)]
    [InlineData(NotificationJobType.LessonRescheduled, false)]
    [InlineData(NotificationJobType.MakeupApproved, false)]
    public void QuietHours_AppliesTo_only_cron_triggered_types(NotificationJobType type, bool expected)
    {
        Assert.Equal(expected, QuietHours.AppliesTo(type));
    }

    [Theory]
    [InlineData("22:00", "21:00", "09:00", true)] // gece yarisini saran pencere icinde
    [InlineData("06:00", "21:00", "09:00", true)]
    [InlineData("12:00", "21:00", "09:00", false)]
    [InlineData("21:00", "21:00", "09:00", true)] // pencere baslangici dahil
    [InlineData("09:00", "21:00", "09:00", false)] // pencere bitisi haric (calisma saatleri basliyor)
    public void QuietHours_IsWithinQuietHours_handles_overnight_window(string local, string start, string end, bool expected)
    {
        var result = QuietHours.IsWithinQuietHours(TimeOnly.Parse(local), TimeOnly.Parse(start), TimeOnly.Parse(end));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void QuietHours_ResolveSendTime_defers_to_next_window_start()
    {
        var schoolTimeZone = TimeZoneInfo.CreateCustomTimeZone("Europe/Istanbul-fixed", TimeSpan.FromHours(3), "Europe/Istanbul-fixed", "Europe/Istanbul-fixed");
        var candidateUtc = new DateTimeOffset(2026, 8, 20, 20, 30, 0, TimeSpan.Zero); // 23:30 yerel, sessiz saat icinde

        var resolved = QuietHours.ResolveSendTime(candidateUtc, schoolTimeZone, TimeOnly.Parse("21:00"), TimeOnly.Parse("09:00"));

        var resolvedLocal = TimeZoneInfo.ConvertTime(resolved, schoolTimeZone);
        Assert.Equal(new DateOnly(2026, 8, 21), DateOnly.FromDateTime(resolvedLocal.Date));
        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(resolvedLocal.DateTime));
    }

    private const string SigningKey = "test-signing-key";

    [Fact]
    public void RsvpButtonPayload_Sign_and_TryVerify_round_trip()
    {
        var lessonId = Guid.NewGuid();

        var payload = RsvpButtonPayload.Sign(RsvpButtonPayload.AttendingAction, lessonId, SigningKey);
        var verified = RsvpButtonPayload.TryVerify(payload, SigningKey, out var action, out var verifiedLessonId);

        Assert.True(verified);
        Assert.Equal(RsvpButtonPayload.AttendingAction, action);
        Assert.Equal(lessonId, verifiedLessonId);
    }

    [Fact]
    public void RsvpButtonPayload_TryVerify_rejects_tampered_lesson_id()
    {
        // Tehdit modeli: Meta'nin kendi imza dogrulamasi yalnizca istegin Meta'dan geldigini
        // kanitlar, buton payload'inin ICERIGINI degil - kotu niyetli bir veli, gercekten
        // kendisine gonderilmis bir butonun imzasini KORUYARAK icindeki ders referansini
        // baska bir dersin UUID'siyle degistirmeye calisabilir. HMAC bunu engeller: imza
        // yeni referenceToken'a gore yeniden hesaplandiginda tutmaz.
        var genuinePayload = RsvpButtonPayload.Sign(RsvpButtonPayload.AttendingAction, Guid.NewGuid(), SigningKey);
        var otherLessonPayload = RsvpButtonPayload.Sign(RsvpButtonPayload.AttendingAction, Guid.NewGuid(), SigningKey);
        var genuineSignature = genuinePayload[(genuinePayload.IndexOf('.') + 1)..];
        var otherReferenceToken = otherLessonPayload[..otherLessonPayload.IndexOf('.')];
        var forged = $"{otherReferenceToken}.{genuineSignature}";

        var verified = RsvpButtonPayload.TryVerify(forged, SigningKey, out _, out _);

        Assert.False(verified);
    }

    [Fact]
    public void RsvpButtonPayload_TryVerify_rejects_wrong_signing_key()
    {
        var payload = RsvpButtonPayload.Sign(RsvpButtonPayload.AttendingAction, Guid.NewGuid(), SigningKey);

        var verified = RsvpButtonPayload.TryVerify(payload, "farkli-anahtar", out _, out _);

        Assert.False(verified);
    }

    [Fact]
    public void RsvpButtonPayload_TryVerify_rejects_when_signing_key_is_empty()
    {
        // SEC-2: WhatsApp__PayloadSigningKey tanimsiz kalirsa (bos string) imza sabit ve
        // tahmin edilebilir hale gelir - Sign de ayni bos anahtarla imzalasa bile TryVerify
        // reddetmeli, aksi halde imzanin var olma amaci (tahmini engellemek) bosa cikar.
        var payload = RsvpButtonPayload.Sign(RsvpButtonPayload.AttendingAction, Guid.NewGuid(), "");

        var verified = RsvpButtonPayload.TryVerify(payload, "", out _, out _);

        Assert.False(verified);
    }

    [Fact]
    public void WebhookSignatureVerifier_IsValid_accepts_matching_signature()
    {
        const string body = """{"hello":"world"}""";
        const string secret = "app-secret";
        var expectedHex = Convert.ToHexStringLower(
            System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(secret), System.Text.Encoding.UTF8.GetBytes(body)));

        Assert.True(WebhookSignatureVerifier.IsValid(body, $"sha256={expectedHex}", secret));
    }

    [Fact]
    public void WebhookSignatureVerifier_IsValid_rejects_wrong_signature()
    {
        Assert.False(WebhookSignatureVerifier.IsValid("""{"hello":"world"}""", "sha256=deadbeef", "app-secret"));
    }

    [Fact]
    public void WebhookSignatureVerifier_IsValid_rejects_missing_or_malformed_header()
    {
        Assert.False(WebhookSignatureVerifier.IsValid("{}", null, "app-secret"));
        Assert.False(WebhookSignatureVerifier.IsValid("{}", "not-a-signature", "app-secret"));
    }

    [Fact]
    public void WebhookSignatureVerifier_IsValid_rejects_when_app_secret_is_empty()
    {
        // SEC-1: WhatsApp__AppSecret tanimsiz kalirsa (bos string) HMAC deterministik/tahmin
        // edilebilir hale gelir - dogru imzayla hesaplansa bile bos secret'ta fail-open olmamali.
        const string body = """{"hello":"world"}""";
        var expectedHexWithEmptySecret = Convert.ToHexStringLower(
            System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(""), System.Text.Encoding.UTF8.GetBytes(body)));

        Assert.False(WebhookSignatureVerifier.IsValid(body, $"sha256={expectedHexWithEmptySecret}", ""));
    }
}
