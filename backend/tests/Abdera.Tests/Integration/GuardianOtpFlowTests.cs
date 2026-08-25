using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

public class GuardianOtpFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private static int _phoneSequence = 1000;
    private readonly AbderaWebApplicationFactory _factory;

    public GuardianOtpFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Otp_is_single_use_and_consumed_code_cannot_create_a_second_session()
    {
        var phone = await SeedGuardianAsync();
        var firstClient = _factory.CreateClient();
        var code = await RequestCodeAsync(firstClient, phone);

        var firstVerify = await firstClient.PostAsJsonAsync(
            "/api/guardian/otp/verify",
            new GuardianAuth.VerifyOtpRequest(phone, code));
        Assert.Equal(HttpStatusCode.OK, firstVerify.StatusCode);

        var me = await firstClient.GetAsync("/api/guardian/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        using var secondClient = _factory.CreateClient();
        var replay = await secondClient.PostAsJsonAsync(
            "/api/guardian/otp/verify",
            new GuardianAuth.VerifyOtpRequest(phone, code));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondClient.GetAsync("/api/guardian/me")).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_attempts_lock_the_code_even_when_correct_code_is_later_supplied()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var phone = await SeedGuardianAsync();
        using var client = _factory.CreateClient();
        var code = await RequestCodeAsync(client, phone);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrongCode = code == "000000" ? "111111" : "000000";
            var response = await client.PostAsJsonAsync(
                "/api/guardian/otp/verify",
                new GuardianAuth.VerifyOtpRequest(phone, wrongCode));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var lockedResponse = await client.PostAsJsonAsync(
            "/api/guardian/otp/verify",
            new GuardianAuth.VerifyOtpRequest(phone, code));

        Assert.Equal(HttpStatusCode.Unauthorized, lockedResponse.StatusCode);
        var guardian = await db.Guardians.SingleAsync(item => item.PhoneNumber == phone);
        var persistedCode = await db.GuardianLoginCodes
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync(item => item.GuardianId == guardian.Id);
        Assert.Equal(5, persistedCode.Attempts);
        Assert.False(persistedCode.IsUsable(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Requesting_a_new_code_makes_the_previous_code_unusable_and_newest_code_succeeds()
    {
        var phone = await SeedGuardianAsync();
        using var client = _factory.CreateClient();
        var firstCode = await RequestCodeAsync(client, phone);
        var secondCode = await RequestCodeAsync(client, phone);

        // Çok düşük olasılıklı aynı OTP üretimini testin davranışını bozmadan tekrar isteyerek geç.
        if (secondCode == firstCode)
        {
            secondCode = await RequestCodeAsync(client, phone);
        }

        var oldCodeResponse = await client.PostAsJsonAsync(
            "/api/guardian/otp/verify",
            new GuardianAuth.VerifyOtpRequest(phone, firstCode));
        Assert.Equal(HttpStatusCode.Unauthorized, oldCodeResponse.StatusCode);

        var newestCodeResponse = await client.PostAsJsonAsync(
            "/api/guardian/otp/verify",
            new GuardianAuth.VerifyOtpRequest(phone, secondCode));
        Assert.Equal(HttpStatusCode.OK, newestCodeResponse.StatusCode);
    }

    [Fact]
    public async Task Unknown_phone_gets_generic_response_without_debug_code_or_persisted_login_code()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var client = _factory.CreateClient();
        var unknownPhone = NextPhone();
        var beforeCount = await db.GuardianLoginCodes.CountAsync();

        var response = await client.PostAsJsonAsync(
            "/api/guardian/otp/request",
            new GuardianAuth.RequestOtpRequest(unknownPhone));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<GuardianAuth.RequestOtpResponse>(TestJson.Options))!;
        Assert.Contains("kayıtlıysa", body.Message);
        Assert.Null(body.DebugCode);
        Assert.Equal(beforeCount, await db.GuardianLoginCodes.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("telefon-değil")]
    public async Task Malformed_phone_is_rejected_before_creating_an_otp(string phone)
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var client = _factory.CreateClient();
        var beforeCount = await db.GuardianLoginCodes.CountAsync();

        var response = await client.PostAsJsonAsync(
            "/api/guardian/otp/request",
            new GuardianAuth.RequestOtpRequest(phone));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(beforeCount, await db.GuardianLoginCodes.CountAsync());
    }

    private async Task<string> SeedGuardianAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var phone = NextPhone();
        db.Guardians.Add(Guardian.Create("OTP", "Test", phone, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return phone;
    }

    private static async Task<string> RequestCodeAsync(HttpClient client, string phone)
    {
        var response = await client.PostAsJsonAsync(
            "/api/guardian/otp/request",
            new GuardianAuth.RequestOtpRequest(phone));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<GuardianAuth.RequestOtpResponse>(TestJson.Options))!;
        return Assert.IsType<string>(body.DebugCode);
    }

    private static string NextPhone()
    {
        var sequence = Interlocked.Increment(ref _phoneSequence);
        return $"+9055800{sequence:D5}";
    }

    // Gerçek bir bug'ın regresyonu: gövdede phoneNumber yok/boş olduğunda uç nokta
    // NullReferenceException ile 500 dönüyordu. Kötü biçimli istek kontrollü 400 vermeli.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"phoneNumber\":null}")]
    [InlineData("{\"phoneNumber\":\"\"}")]
    [InlineData("{\"phoneNumber\":\"   \"}")]
    public async Task Otp_request_with_a_missing_phone_number_is_a_validation_error_not_a_server_error(string body)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/guardian/otp/request",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Aynı sınıf hata admin ucunda da vardı: geçersiz telefonla veli oluşturmak 500 dönüyordu.
    [Fact]
    public async Task Creating_a_guardian_with_an_invalid_phone_number_is_a_validation_error_not_a_server_error()
    {
        var admin = _factory.CreateClient();
        (await admin.PostAsJsonAsync(
            "/api/auth/login",
            new Abdera.Api.Modules.Auth.Features.Login.Request("admin@test.local", "Test1234!")))
            .EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync(
            "/api/guardians",
            new Guardians.CreateRequest("Test", "Veli", "gecersiz-numara"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Gerçek bir bug'ın regresyonu: eksik/geçersiz query parametresi Minimal API'nin
    // BadHttpRequestException'ını üretiyor, bu da global handler'da yakalanmadığı için
    // istemci hatası 500 olarak dönüyordu. İstemci hatası 4xx olmalı - aksi halde gerçek
    // sunucu hataları da loglarda bu gürültünün içinde kayboluyor.
    [Fact]
    public async Task A_missing_required_query_parameter_is_a_client_error_not_a_server_error()
    {
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync(
            "/api/auth/login",
            new Abdera.Api.Modules.Auth.Features.Login.Request("admin@test.local", "Test1234!")))
            .EnsureSuccessStatusCode();

        // from/to zorunlu ama gönderilmiyor.
        var response = await client.GetAsync("/api/calendar");

        Assert.True(
            (int)response.StatusCode >= 400 && (int)response.StatusCode < 500,
            $"eksik parametre için 4xx bekleniyordu, {(int)response.StatusCode} döndü");
    }
}
