using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Abdera.Tests.Integration;

// docs/09-testing.md - Phase 1'in uçtan uca karşılığı: bootstrap admin ile giriş,
// oturum çerezinin /me'de tanınması, şifre değişimi ve çıkış.
public class AuthFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public AuthFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Bootstrap_admin_can_login_then_me_returns_profile()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new Login.Request("admin@test.local", "Test1234!"));

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<Login.Response>(TestJson.Options);
        Assert.NotNull(loginBody);
        Assert.True(loginBody!.MustChangePassword); // bootstrap admin ilk girişte şifre değiştirmeli

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<Me.Response>(TestJson.Options);
        Assert.Equal("admin@test.local", me!.Email);
    }

    [Fact]
    public async Task Wrong_password_returns_401_without_revealing_which_field_was_wrong()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new Login.Request("admin@test.local", "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_without_session_returns_401()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Change_password_then_old_password_no_longer_works()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"));

        var changeResponse = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePassword.Request("Test1234!", "YeniSifre2026!"));
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        using var freshClient = _factory.CreateClient();
        var oldLogin = await freshClient.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await freshClient.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "YeniSifre2026!"));
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Login_returns_429_after_exceeding_rate_limit()
    {
        // SEC-3: paylaşılan AbderaWebApplicationFactory login limitini testler bozulmasın
        // diye pratikte sınırsız yapıyor (RateLimiting:LoginPermitLimit=10000) - asıl
        // davranışı doğrulamak için burada düşük bir limitle ayrı bir host kuruyoruz.
        using var limitedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:LoginPermitLimit"] = "3",
                ["RateLimiting:LoginWindowMinutes"] = "15",
            }));
        });
        using var client = limitedFactory.CreateClient();

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            response = await client.PostAsJsonAsync("/api/auth/login",
                new Login.Request("admin@test.local", "wrong-password"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
    }
}
