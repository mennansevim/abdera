using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
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
        var sessionCookie = loginResponse.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("expires=", sessionCookie.ToLowerInvariant());
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

        // Bu sınıftaki testler aynı gerçek Postgres'i (IClassFixture) paylaşıyor - şifreyi
        // geri almazsak, "Test1234!" ile giriş yapan sonraki testler bu testin çalışma
        // sırasına sessizce bağımlı kalırdı. Geri almak sıra bağımlılığını ortadan kaldırıyor.
        var revert = await freshClient.PostAsJsonAsync("/api/auth/change-password",
            new ChangePassword.Request("YeniSifre2026!", "Test1234!"));
        Assert.Equal(HttpStatusCode.NoContent, revert.StatusCode);
    }

    // Canlı QA turunda bulunan gerçek bug: SignOutAsync yalnızca istemci cookie'sini
    // siliyordu - imzalı ticket kendiliğinden geçersiz olmuyordu, bu yüzden bir kopyası
    // alınmış eski cookie logout sonrasında da sliding pencere (12 saat) boyunca hâlâ
    // kabul ediliyordu. SecurityStamp doğrulaması (Program.cs OnValidatePrincipal) bunu
    // düzeltiyor - burada tam olarak "kopyalanmış cookie" senaryosunu test ediyoruz.
    [Fact]
    public async Task Logout_invalidates_the_session_cookie_even_for_a_copy_kept_elsewhere()
    {
        await using var db = await _factory.CreateDbContextAsync();
        // HandleCookies=false: cookie'yi otomatik bir kavanoza koyup gizlemek yerine ham
        // Set-Cookie başlığını okuyup elle taşıyoruz - "bir kopyası alınmış cookie" senaryosunu
        // gerçekten simüle etmenin tek yolu bu (aksi halde istemcinin kendi kavanozu logout
        // sonrası cookie'yi zaten kendiliğinden temizlerdi).
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new Login.Request("admin@test.local", "Test1234!"));
        var cookieValue = loginResponse.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Add("Cookie", cookieValue);
        var meBeforeLogout = await client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, meBeforeLogout.StatusCode);

        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("Cookie", cookieValue);
        var logoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var meAfterLogoutRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meAfterLogoutRequest.Headers.Add("Cookie", cookieValue);
        var meAfterLogout = await client.SendAsync(meAfterLogoutRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);
    }

    // Aynı sınıf bug: şifre değiştirildiğinde, değişiklikten ÖNCE alınmış bir cookie de
    // hâlâ geçerliydi - kullanıcı "şifremi değiştirdim" dediği anda diğer tüm oturumların
    // düşmesini bekler.
    [Fact]
    public async Task Changing_password_invalidates_a_session_cookie_obtained_before_the_change()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new Login.Request("admin@test.local", "Test1234!"));
        var cookieValue = loginResponse.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        var changeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePassword.Request("Test1234!", "StampTest2026!")),
        };
        changeRequest.Headers.Add("Cookie", cookieValue);
        var changeResponse = await client.SendAsync(changeRequest);
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        var meAfterChangeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meAfterChangeRequest.Headers.Add("Cookie", cookieValue);
        var meAfterChange = await client.SendAsync(meAfterChangeRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterChange.StatusCode);

        // Test veritabanını sonraki testler için eski şifreye geri al (yeni şifreyle
        // giriş yapıp yeni bir cookie almak gerekiyor - eskisi artık geçersiz).
        var reLoginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new Login.Request("admin@test.local", "StampTest2026!"));
        var newCookieValue = reLoginResponse.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var revertRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePassword.Request("StampTest2026!", "Test1234!")),
        };
        revertRequest.Headers.Add("Cookie", newCookieValue);
        var revert = await client.SendAsync(revertRequest);
        Assert.Equal(HttpStatusCode.NoContent, revert.StatusCode);
    }

    [Fact]
    public async Task Login_with_nonexistent_email_returns_the_same_response_as_wrong_password()
    {
        // SEC-4: kullanıcı numaralandırmasına karşı - var olmayan e-posta ile kayıtlı bir
        // e-postaya yanlış şifre girmek aynı görünür yanıtı vermeli (durum kodu + gövde).
        // Zamanlama farkının kapatılması (dummy hash doğrulaması) burada otomatik test
        // edilmiyor - CI'da güvenilir bir eşik belirlemek flaky olurdu; Login.cs'teki
        // yorum bu invariant'ı ve nedenini açıklıyor.
        using var client = _factory.CreateClient();

        var wrongPasswordResponse = await client.PostAsJsonAsync("/api/auth/login",
            new Login.Request("admin@test.local", "wrong-password"));
        var nonexistentEmailResponse = await client.PostAsJsonAsync("/api/auth/login",
            new Login.Request("hic-boyle-bir-kullanici-yok@test.local", "herhangi-bir-sifre"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, nonexistentEmailResponse.StatusCode);

        // ProblemDetails'in traceId'si her istekte farklı olduğundan tüm gövdeyi değil,
        // görünür alanları (title/detail) karşılaştırıyoruz - kullanıcı numaralandırmasına
        // karşı asıl önem taşıyan bunlar.
        var wrongPasswordBody = await wrongPasswordResponse.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        var nonexistentEmailBody = await nonexistentEmailResponse.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        Assert.Equal(wrongPasswordBody!.Title, nonexistentEmailBody!.Title);
        Assert.Equal(wrongPasswordBody.Detail, nonexistentEmailBody.Detail);
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

        // Gövde boş dönerse arayüz yalnızca "Bir hata oluştu" gösterebiliyordu: kullanıcı ne
        // olduğunu ve ne kadar bekleyeceğini öğrenemiyordu. Reddedilen istek de diğer hatalar
        // gibi ProblemDetails taşımalı ve Retry-After vermeli.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        Assert.Equal("Çok fazla deneme", problem!.Title);
        Assert.Contains("tekrar dene", problem.Detail);
        Assert.True(response.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.True(int.Parse(retryAfter!.Single()) > 0);
    }

    // Giriş ekranındaki rol seçimi (Yöneticiyim/Öğretmenim) yalnızca görsel bir tercihti:
    // "Yöneticiyim" seçip öğretmen bilgilerini girmek sessizce öğretmen oturumu açıyordu.
    // Artık seçim sunucuya gidiyor ve uyuşmazlıkta oturum HİÇ açılmıyor - bu testin asıl
    // kontrolü 403 değil, ardından /me'nin hâlâ 401 dönmesi.
    [Fact]
    public async Task Choosing_admin_but_using_teacher_credentials_is_rejected_without_opening_a_session()
    {
        using var admin = _factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();

        var instruments = await (await admin.GetAsync("/api/instruments")).Content
            .ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var email = $"rolecheck.{Guid.NewGuid():N}@test.local";
        var createResponse = await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest("Rol", "Kontrol", [instruments!.First().Id], email));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options);
        var password = created!.TemporaryPassword!;

        using var client = _factory.CreateClient();
        var mismatched = await client.PostAsJsonAsync("/api/auth/login",
            new Login.Request(email, password, UserRole.Admin));

        Assert.Equal(HttpStatusCode.Forbidden, mismatched.StatusCode);
        Assert.False(mismatched.Headers.Contains("Set-Cookie"));
        var problem = await mismatched.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        Assert.Contains("Öğretmenim", problem!.Detail);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        // Doğru rol seçildiğinde aynı bilgilerle giriş çalışmalı - kontrol fazla kısıtlayıcı olmamalı.
        var matched = await client.PostAsJsonAsync("/api/auth/login",
            new Login.Request(email, password, UserRole.Teacher));
        Assert.Equal(HttpStatusCode.OK, matched.StatusCode);
        var me = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<Me.Response>(TestJson.Options);
        Assert.Equal(UserRole.Teacher, me!.Role);
    }

    // Yanlış şifre + yanlış rol: rol denetimi şifre doğrulamasının ARDINDAN çalışmalı,
    // yoksa yanlış şifreyle gelen biri hesabın rolünü (dolayısıyla varlığını) öğrenirdi.
    [Fact]
    public async Task Role_mismatch_is_not_revealed_when_the_password_is_wrong()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new Login.Request("admin@test.local", "wrong-password", UserRole.Teacher));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        Assert.Equal("Giriş başarısız", problem!.Title);
    }

    // Yerel hızlı giriş (DevLogin): şifresiz oturum açtığı için yalnızca Development + açık
    // bayrakla haritalanır. Paylaşılan factory Development'ta çalıştığından ilk test,
    // ortam adının TEK BAŞINA bu yolu açmadığını doğrular.
    [Fact]
    public async Task Dev_login_is_not_mapped_unless_explicitly_enabled()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/dev/auth/login", new DevLogin.Request(UserRole.Admin));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Dev_accounts_list_is_only_available_with_dev_login()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var plainClient = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await plainClient.GetAsync("/api/dev/auth/accounts")).StatusCode);

        using var devFactory = WithDevLogin(_factory);
        using var client = devFactory.CreateClient();
        var accounts = await client.GetFromJsonAsync<List<DevLogin.Account>>("/api/dev/auth/accounts", TestJson.Options);

        Assert.Contains(accounts!, a => a.Email == "admin@test.local" && a.Role == UserRole.Admin);
    }

    [Fact]
    public async Task Dev_login_opens_a_session_for_the_first_active_account_of_the_chosen_role()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var devFactory = WithDevLogin(_factory);
        using var client = devFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/dev/auth/login", new DevLogin.Request(UserRole.Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Login.Response>(TestJson.Options);
        Assert.False(body!.MustChangePassword); // yerelde her girişte şifre değiştirmeye yönlendirmez
        var me = await client.GetFromJsonAsync<Me.Response>("/api/auth/me", TestJson.Options);
        Assert.Equal("admin@test.local", me!.Email);
        Assert.True(me.MustChangePassword); // hesaptaki bayrak değişmedi
    }

    [Fact]
    public async Task Dev_login_keeps_the_role_choice_binding()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var devFactory = WithDevLogin(_factory);
        using var client = devFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/dev/auth/login",
            new DevLogin.Request(UserRole.Teacher, "admin@test.local"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Dev_login_stays_unmapped_outside_development_even_when_the_flag_is_on()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var stagingFactory = WithDevLogin(_factory).WithWebHostBuilder(builder => builder.UseEnvironment("Staging"));
        using var client = stagingFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/dev/auth/login", new DevLogin.Request(UserRole.Admin));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> WithDevLogin(WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:DevLogin:Enabled"] = "true" })));
}
