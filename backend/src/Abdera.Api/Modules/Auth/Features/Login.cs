using System.Security.Claims;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Auth.Features;

public static class Login
{
    // ExpectedRole: giriş ekranındaki rol seçimi (Yöneticiyim/Öğretmenim). İstemci hangi
    // çalışma alanına girmek istediğini bildirir; kimlik doğrulandıktan SONRA hesabın
    // gerçek rolüyle karşılaştırılır. Uyuşmazsa oturum hiç açılmaz - aksi halde "Yönetici"
    // seçip öğretmen bilgileriyle giriş yapmak sessizce öğretmen oturumu açıyordu.
    // Opsiyonel: rol seçimi olmayan istemciler (testler, e2e'nin doğrudan API çağrısı)
    // null gönderir ve davranış eskisi gibi kalır.
    public record Request(string Email, string Password, UserRole? ExpectedRole = null);

    public record Response(Guid Id, string Email, UserRole Role, bool MustChangePassword);

    // SEC-4 (docs/13-audit-fix-prompt.md): kullanıcı bulunamadığında da hash doğrulamasının
    // ÇALIŞTIRILMASI için sabit, önceden hesaplanmış bir "dummy" kullanıcı/hash - aşağıdaki
    // dummy şifre yalnızca bu hash'i üretmek için kullanılıyor, gerçek bir hesaba ait değil.
    private static readonly User DummyUser = User.Create("dummy@internal.local", "", UserRole.Admin, DateTimeOffset.UnixEpoch);
    private static readonly string DummyPasswordHash =
        new PasswordHasher<User>().HashPassword(DummyUser, "dummy-password-for-timing-safety-only");

    public static void MapLogin(this IEndpointRouteBuilder app)
    {
        // SEC-3: kaba kuvvet korumasi - IP basina sabit pencere (bkz. Program.cs "auth-login" politikasi).
        app.MapPost("/api/auth/login", HandleAsync).AllowAnonymous().RequireRateLimiting("auth-login");
    }

    private static async Task<IResult> HandleAsync(
        Request request,
        AbderaDbContext db,
        IPasswordHasher<User> passwordHasher,
        HttpContext httpContext,
        ILogger<Program> logger)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email && u.IsActive);

        if (user is null)
        {
            // Var olmayan e-posta ile yanlış şifre arasında yanıt mesajı zaten aynıydı, ama
            // hash doğrulama adımı hiç çalışmadığından SÜRE farklıydı (kayıtlı e-posta ~50-100ms,
            // kayıtsız ~1ms) - bu da kullanıcı numaralandırmasına yeten bir zamanlama kanalıydı.
            // Sonucu kullanılmasa da sabit bir dummy hash'e karşı doğrulama çalıştırarak süre
            // eşitleniyor.
            passwordHasher.VerifyHashedPassword(DummyUser, DummyPasswordHash, request.Password);
            return Results.Problem(statusCode: 401, title: "Giriş başarısız", detail: "E-posta veya şifre hatalı.");
        }

        var verifyResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Başarısız giriş denemesi: {Email}", email);
            return Results.Problem(statusCode: 401, title: "Giriş başarısız", detail: "E-posta veya şifre hatalı.");
        }

        // Rol seçimi yalnızca şifre doğrulandıktan sonra denetlenir: yanlış şifreyle gelen
        // bir istek hiçbir zaman hesabın rolünü öğrenemez (kullanıcı numaralandırma kanalı
        // açılmaz). Doğru şifreyi bilen kişiye ise kendi rolünü söylemek bilgi sızdırmaz.
        if (request.ExpectedRole is { } expectedRole && expectedRole != user.Role)
        {
            logger.LogWarning("Rol uyuşmazlığı: {Email} {ExpectedRole} seçti, hesap {ActualRole}.", email, expectedRole, user.Role);
            return Results.Problem(
                statusCode: 403,
                title: "Rol uyuşmuyor",
                detail: $"Bu hesap {RoleLabel(user.Role)} hesabı. Lütfen \"{RoleChoiceLabel(user.Role)}\" seçeneğiyle giriş yap.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(SecurityStampClaim.ClaimType, user.SecurityStamp.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        // Kalıcı cookie tarayıcı kapatılıp yeniden açıldığında da rolü hatırlar. Personel
        // oturumu yine Program.cs'teki kısa Auth:SessionHours süresiyle sınırlıdır; güvenlik
        // damgası değişirse veya çıkış yapılırsa sunucu cookie'yi anında reddeder.
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = true,
        };
        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);

        return Results.Ok(new Response(user.Id, user.Email, user.Role, user.MustChangePassword));
    }

    // Kullanıcıya görünen metin Türkçe (CLAUDE.md "Dil"). Rol adları hata mesajında
    // geçtiği için burada tutuluyor; giriş ekranındaki kart başlıklarıyla birebir aynı
    // olmalı ki kullanıcı hangi seçeneğe basacağını arayıp bulmasın.
    private static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Admin => "bir yönetici",
        UserRole.Teacher => "bir öğretmen",
        _ => "bir veli",
    };

    private static string RoleChoiceLabel(UserRole role) => role switch
    {
        UserRole.Admin => "Yöneticiyim",
        UserRole.Teacher => "Öğretmenim",
        _ => "Veliyim",
    };
}
