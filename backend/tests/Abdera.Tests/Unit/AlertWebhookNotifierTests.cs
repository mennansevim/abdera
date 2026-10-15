using System.Net;
using System.Security.Cryptography;
using System.Text;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Abdera.Tests.Unit;

// A7: okul alarmı platforma imzalı JSON olarak gider; URL yoksa hiç istek atılmaz, uç hata
// verirse alarm akışı bozulmaz.
public class AlertWebhookNotifierTests
{
    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static AlertWebhookNotifier Notifier(RecordingHandler handler, Dictionary<string, string?> settings) =>
        new(new Factory(handler), new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), NullLogger<AlertWebhookNotifier>.Instance);

    [Fact]
    public async Task Signed_alert_is_posted_without_school_identity()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        var notifier = Notifier(handler, new()
        {
            ["Alerts:WebhookUrl"] = "https://platform.example/alerts",
            ["Alerts:WebhookSecret"] = "alerts-secret-0123456789abcdef0123456789",
            ["App:InstanceSlug"] = "deniz",
        });

        await notifier.NotifyAsync("health", SystemHealthLevel.Unhealthy, "Veritabanına ulaşılamıyor.", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull(handler.Request);
        Assert.Contains("\"instance\":\"deniz\"", handler.Body);
        var expected = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("alerts-secret-0123456789abcdef0123456789"), Encoding.UTF8.GetBytes(handler.Body!))).ToLowerInvariant();
        Assert.Equal($"sha256={expected}", handler.Request!.Headers.GetValues(AlertWebhookNotifier.SignatureHeader).Single());
    }

    [Fact]
    public async Task Without_url_nothing_is_sent_and_failures_never_throw()
    {
        var silent = new RecordingHandler(HttpStatusCode.OK);
        await Notifier(silent, new()).NotifyAsync("health", SystemHealthLevel.Degraded, "x", DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Null(silent.Request);

        var failing = new RecordingHandler(HttpStatusCode.InternalServerError);
        await Notifier(failing, new() { ["Alerts:WebhookUrl"] = "https://platform.example/alerts" })
            .NotifyAsync("backup_failed", SystemHealthLevel.Degraded, "x", DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.NotNull(failing.Request);
    }
}
