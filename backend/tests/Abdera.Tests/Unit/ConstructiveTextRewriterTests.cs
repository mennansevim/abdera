using System.Net;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Progress.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Abdera.Tests.Unit;

// Faz 10 "yapıcı metne dönüştür". Asıl korunan davranış: AI yapılandırılmamışken veya
// sağlayıcı hata verirken uygulama SESSİZCE bozulmasın - özellik kapalı kalsın, öğretmen
// yorumu elle yazmaya devam edebilsin (feature_targets.md Faz 10, madde 8.6).
public class ConstructiveTextRewriterTests
{
    private static OpenAiConstructiveTextRewriter CreateOpenAi(AiOptions options, HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(options), NullLogger<OpenAiConstructiveTextRewriter>.Instance);

    private static AiOptions ConfiguredOptions() => new()
    {
        Provider = "OpenAi",
        ApiKey = "test-api-key",
        BaseUrl = "https://ai.test/v1",
        Model = "test-model",
    };

    private static ConstructiveRewriteRequest SampleRequest() =>
        new("tempoyu tutamadı, sol el zayıf", "Lara", "Minuet in G");

    [Fact]
    public void Disabled_rewriter_reports_itself_as_unavailable()
    {
        var rewriter = new DisabledConstructiveTextRewriter();

        Assert.False(rewriter.IsAvailable);
    }

    [Fact]
    public async Task Disabled_rewriter_fails_cleanly_instead_of_throwing()
    {
        // Bu uç nokta hiç çağrılmamalı ama çağrılırsa da 500 değil, açıklayıcı bir sonuç dönmeli.
        var result = await new DisabledConstructiveTextRewriter().RewriteAsync(SampleRequest());

        Assert.False(result.Success);
        Assert.Null(result.Suggestion);
        Assert.Contains("AI sağlayıcısı", result.Error);
    }

    [Fact]
    public void Openai_rewriter_is_unavailable_without_an_api_key()
    {
        var options = ConfiguredOptions();
        options.ApiKey = "";

        var rewriter = CreateOpenAi(options, new StubHandler(_ => throw new InvalidOperationException("çağrılmamalıydı")));

        Assert.False(rewriter.IsAvailable);
    }

    [Fact]
    public async Task Openai_rewriter_does_not_call_the_provider_without_an_api_key()
    {
        var options = ConfiguredOptions();
        options.ApiKey = "";
        var handler = new StubHandler(_ => throw new InvalidOperationException("anahtar yokken ağ çağrısı yapılmamalı"));

        var result = await CreateOpenAi(options, handler).RewriteAsync(SampleRequest());

        Assert.False(result.Success);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Openai_rewriter_returns_the_suggestion_from_a_successful_response()
    {
        const string suggestion = "Lara bu hafta Minuet in G üzerinde istikrarlı çalıştı.";
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"S"}}]}""".Replace("S", suggestion)));

        var result = await CreateOpenAi(ConfiguredOptions(), handler).RewriteAsync(SampleRequest());

        Assert.True(result.Success);
        Assert.Equal(suggestion, result.Suggestion);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Openai_rewriter_sends_the_raw_note_and_configured_model_to_the_provider()
    {
        string? body = null;
        Uri? requestUri = null;
        var handler = new StubHandler(request =>
        {
            requestUri = request.RequestUri;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"öneri"}}]}""");
        });

        await CreateOpenAi(ConfiguredOptions(), handler).RewriteAsync(SampleRequest());

        // BaseUrl konfigürasyondan gelir - OpenAI uyumlu başka bir gateway de kullanılabilsin.
        Assert.Equal("https://ai.test/v1/chat/completions", requestUri!.ToString());
        Assert.Contains("test-model", body);
        // Sadece ASCII on ek aranir: JsonContent.Create varsayilan olarak ASCII disi
        // karakterleri kacirir ("tutamadi" -> "tutamad\u0131"). Bu gecerli JSON'dur ve
        // saglayici dogru cozer - testin bu kodlama detayina bagimli olmasi gereksiz.
        Assert.Contains("tempoyu tutamad", body);
    }

    [Fact]
    public async Task Provider_error_status_becomes_a_failed_result_not_an_exception()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.TooManyRequests, """{"error":"rate limit"}"""));

        var result = await CreateOpenAi(ConfiguredOptions(), handler).RewriteAsync(SampleRequest());

        Assert.False(result.Success);
        Assert.Null(result.Suggestion);
        Assert.Contains("429", result.Error);
    }

    [Fact]
    public async Task Provider_error_body_is_not_leaked_to_the_caller()
    {
        // Sağlayıcı gövdesi anahtar/kota ayrıntısı içerebilir; kullanıcıya gitmemeli.
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.Unauthorized,
            """{"error":{"message":"Incorrect API key provided: sk-secret123"}}"""));

        var result = await CreateOpenAi(ConfiguredOptions(), handler).RewriteAsync(SampleRequest());

        Assert.False(result.Success);
        Assert.DoesNotContain("sk-secret123", result.Error);
    }

    [Fact]
    public async Task An_empty_completion_is_treated_as_failure_rather_than_an_empty_suggestion()
    {
        // Boş bir öneriyi "başarılı" saymak öğretmenin karşısına boş bir kutu çıkarırdı.
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"   "}}]}"""));

        var result = await CreateOpenAi(ConfiguredOptions(), handler).RewriteAsync(SampleRequest());

        Assert.False(result.Success);
    }

    [Fact]
    public async Task A_transport_failure_becomes_a_failed_result_not_an_exception()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("bağlantı kurulamadı"));

        var result = await CreateOpenAi(ConfiguredOptions(), handler).RewriteAsync(SampleRequest());

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
