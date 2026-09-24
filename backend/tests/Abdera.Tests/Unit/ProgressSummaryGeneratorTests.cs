using System.Net;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Progress.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Abdera.Tests.Unit;

// "Genel gelişim" AI yorumu. Asıl korunan davranış: AI yapılandırılmamışken veya sağlayıcı hata
// verirken ekran SESSİZCE bozulmasın ve asla boş/uydurma bir metin "yorum" diye sunulmasın.
public class ProgressSummaryGeneratorTests
{
    private static OpenAiProgressSummaryGenerator CreateOpenAi(AiOptions options, HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(options), NullLogger<OpenAiProgressSummaryGenerator>.Instance);

    private static AiOptions ConfiguredOptions() => new()
    {
        Provider = "OpenAi",
        ApiKey = "test-api-key",
        BaseUrl = "https://ai.test/v1",
        Model = "test-model",
    };

    private static ProgressSummaryNote Note(int day, string note) => new(
        new DateTimeOffset(2026, 9, day, 16, 30, 0, TimeSpan.Zero), "Piyano", "Minuet in G", 2,
        "Sol el gamı", note, "Metronomla 10 dakika", "80 BPM");

    private static ProgressSummaryRequest SampleRequest() =>
        new("Lara", [Note(1, "tempoyu tutamadı"), Note(8, "tempo oturdu")]);

    [Fact]
    public async Task Disabled_generator_is_unavailable_and_fails_cleanly()
    {
        var generator = new DisabledProgressSummaryGenerator();

        var result = await generator.GenerateAsync(SampleRequest());

        Assert.False(generator.IsAvailable);
        Assert.False(result.Success);
        Assert.Null(result.Summary);
        Assert.Contains("AI sağlayıcısı", result.Error);
    }

    [Fact]
    public async Task Openai_generator_does_not_call_the_provider_without_an_api_key()
    {
        var options = ConfiguredOptions();
        options.ApiKey = "";
        var handler = new StubHandler(_ => throw new InvalidOperationException("anahtar yokken ağ çağrısı yapılmamalı"));
        var generator = CreateOpenAi(options, handler);

        var result = await generator.GenerateAsync(SampleRequest());

        Assert.False(generator.IsAvailable);
        Assert.False(result.Success);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Openai_generator_returns_the_summary_from_a_successful_response()
    {
        const string summary = "Lara tempo kontrolünde belirgin ilerleme gösteriyor.";
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"  S  "}}]}""".Replace("S", summary)));

        var result = await CreateOpenAi(ConfiguredOptions(), handler).GenerateAsync(SampleRequest());

        Assert.True(result.Success);
        Assert.Equal(summary, result.Summary);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Openai_generator_sends_every_note_in_order_with_the_configured_model()
    {
        string? body = null;
        Uri? requestUri = null;
        var handler = new StubHandler(request =>
        {
            requestUri = request.RequestUri;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"yorum"}}]}""");
        });

        await CreateOpenAi(ConfiguredOptions(), handler).GenerateAsync(SampleRequest());

        // BaseUrl konfigürasyondan gelir - OpenAI uyumlu başka bir gateway de kullanılabilsin.
        Assert.Equal("https://ai.test/v1/chat/completions", requestUri!.ToString());
        Assert.Contains("test-model", body);
        // Tarih kültürden bağımsız ISO biçiminde; eski not yeniden önce gelir. (JsonContent ASCII
        // dışı karakterleri kaçırır, bu yüzden yalnızca ASCII parçalar aranır.)
        var older = body!.IndexOf("2026-09-01", StringComparison.Ordinal);
        var newer = body.IndexOf("2026-09-08", StringComparison.Ordinal);
        Assert.True(older >= 0 && newer > older);
        Assert.Contains("tempoyu tutamad", body);
    }

    [Fact]
    public void User_message_keeps_only_the_most_recent_notes()
    {
        var notes = Enumerable.Range(1, OpenAiProgressSummaryGenerator.MaxNotes + 5)
            .Select(index => Note(1, $"not-{index:00}"))
            .ToList();

        var message = OpenAiProgressSummaryGenerator.BuildUserMessage(new ProgressSummaryRequest("Lara", notes));

        Assert.DoesNotContain("not-01", message);
        Assert.Contains($"not-{OpenAiProgressSummaryGenerator.MaxNotes + 5:00}", message);
    }

    [Fact]
    public async Task Provider_error_is_a_failed_result_that_does_not_leak_the_body()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.Unauthorized,
            """{"error":{"message":"Incorrect API key provided: sk-secret123"}}"""));

        var result = await CreateOpenAi(ConfiguredOptions(), handler).GenerateAsync(SampleRequest());

        Assert.False(result.Success);
        Assert.Contains("401", result.Error);
        Assert.DoesNotContain("sk-secret123", result.Error);
    }

    [Fact]
    public async Task An_empty_completion_is_treated_as_failure()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"   "}}]}"""));

        var result = await CreateOpenAi(ConfiguredOptions(), handler).GenerateAsync(SampleRequest());

        Assert.False(result.Success);
    }

    [Fact]
    public async Task A_transport_failure_becomes_a_failed_result_not_an_exception()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("bağlantı kurulamadı"));

        var result = await CreateOpenAi(ConfiguredOptions(), handler).GenerateAsync(SampleRequest());

        Assert.False(result.Success);
        Assert.Contains("ulaşılamadı", result.Error);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(respond(request));
        }
    }
}
