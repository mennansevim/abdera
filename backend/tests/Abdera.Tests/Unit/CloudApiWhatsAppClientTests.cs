using System.Net;
using System.Text;
using System.Text.Json;
using Abdera.Api.Modules.Messaging.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Abdera.Tests.Unit;

public class CloudApiWhatsAppClientTests
{
    private static readonly WhatsAppOptions Options = new()
    {
        Provider = "Cloud",
        ApiVersion = "v99.0",
        PhoneNumberId = "phone-number-id",
        AccessToken = "test-access-token",
    };

    [Fact]
    public async Task SendTemplateAsync_builds_Meta_contract_with_ordered_body_and_quick_reply_components()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"messages":[{"id":"wamid.template-1"}]}"""));
        var client = CreateClient(handler);
        var parameters = new Dictionary<string, string>
        {
            ["guardian_name"] = "Ayşe",
            ["lesson_time"] = "25 Ağustos 17:00",
        };

        var result = await client.SendTemplateAsync(
            "+905551234567",
            "lesson_reminder_rsvp",
            parameters,
            ["signed-attending", "signed-late", "signed-not-attending"]);

        Assert.True(result.Success);
        Assert.Equal("wamid.template-1", result.ProviderMessageId);
        Assert.Null(result.Error);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://graph.facebook.com/v99.0/phone-number-id/messages",
            request.Uri.ToString());
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("test-access-token", request.AuthorizationParameter);

        using var document = JsonDocument.Parse(request.Body);
        var root = document.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("+905551234567", root.GetProperty("to").GetString());
        Assert.Equal("template", root.GetProperty("type").GetString());

        var template = root.GetProperty("template");
        Assert.Equal("lesson_reminder_rsvp", template.GetProperty("name").GetString());
        Assert.Equal("tr", template.GetProperty("language").GetProperty("code").GetString());

        var components = template.GetProperty("components").EnumerateArray().ToArray();
        Assert.Equal(4, components.Length);
        Assert.Equal("body", components[0].GetProperty("type").GetString());
        Assert.Equal(
            ["Ayşe", "25 Ağustos 17:00"],
            components[0].GetProperty("parameters").EnumerateArray()
                .Select(item => item.GetProperty("text").GetString()!)
                .ToArray());

        for (var index = 1; index < components.Length; index++)
        {
            var button = components[index];
            Assert.Equal("button", button.GetProperty("type").GetString());
            Assert.Equal("quick_reply", button.GetProperty("sub_type").GetString());
            Assert.Equal((index - 1).ToString(), button.GetProperty("index").GetString());
        }

        Assert.Equal(
            ["signed-attending", "signed-late", "signed-not-attending"],
            components.Skip(1)
                .Select(button => button.GetProperty("parameters")[0].GetProperty("payload").GetString()!)
                .ToArray());
    }

    [Fact]
    public async Task SendTemplateAsync_omits_button_components_when_no_payloads_are_supplied()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"messages":[{"id":"wamid.no-buttons"}]}"""));
        var client = CreateClient(handler);

        var result = await client.SendTemplateAsync(
            "+905551234567",
            "payment_reminder",
            new Dictionary<string, string> { ["amount"] = "2.000,00" });

        Assert.True(result.Success);
        using var document = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var components = document.RootElement.GetProperty("template").GetProperty("components");
        Assert.Equal(1, components.GetArrayLength());
        Assert.Equal("body", components[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task SendFreeTextAsync_builds_text_contract_and_returns_provider_message_id()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"messages":[{"id":"wamid.text-1"}]}"""));
        var client = CreateClient(handler);

        var result = await client.SendFreeTextAsync("+905551234567", "Dersiniz yarın 17:00'de.");

        Assert.True(result.Success);
        Assert.Equal("wamid.text-1", result.ProviderMessageId);
        using var document = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var root = document.RootElement;
        Assert.Equal("text", root.GetProperty("type").GetString());
        Assert.Equal("Dersiniz yarın 17:00'de.", root.GetProperty("text").GetProperty("body").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "HTTP 400")]
    [InlineData(HttpStatusCode.Unauthorized, "HTTP 401")]
    [InlineData(HttpStatusCode.TooManyRequests, "HTTP 429")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    public async Task SendFreeTextAsync_maps_non_success_responses_to_retryable_failure(
        HttpStatusCode statusCode,
        string expectedError)
    {
        var handler = new RecordingHandler(_ => JsonResponse(statusCode, """{"error":{"message":"provider error"}}"""));
        var client = CreateClient(handler);

        var result = await client.SendFreeTextAsync("+905551234567", "test");

        Assert.False(result.Success);
        Assert.Null(result.ProviderMessageId);
        Assert.Equal(expectedError, result.Error);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"messages\":[]}")]
    [InlineData("{\"messages\":[{\"id\":\"\"}]}")]
    public async Task SendFreeTextAsync_rejects_2xx_response_without_provider_message_id(string responseBody)
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, responseBody));
        var client = CreateClient(handler);

        var result = await client.SendFreeTextAsync("+905551234567", "test");

        Assert.False(result.Success);
        Assert.Null(result.ProviderMessageId);
        Assert.Equal("Sağlayıcı mesaj kimliği dönmedi.", result.Error);
    }

    [Fact]
    public async Task SendFreeTextAsync_maps_transport_exception_to_failure_without_throwing()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("network unavailable"));
        var client = CreateClient(handler);

        var result = await client.SendFreeTextAsync("+905551234567", "test");

        Assert.False(result.Success);
        Assert.Null(result.ProviderMessageId);
        Assert.Contains("network unavailable", result.Error);
    }

    [Fact]
    public async Task SendFreeTextAsync_propagates_caller_cancellation()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendFreeTextAsync("+905551234567", "test", cancellation.Token));
    }

    private static CloudApiWhatsAppClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Microsoft.Extensions.Options.Options.Create(Options),
        NullLogger<CloudApiWhatsAppClient>.Instance);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responseFactory(request);
        }
    }

    private record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string Body);
}
