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
    public record Request(string Email, string Password);

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

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return Results.Ok(new Response(user.Id, user.Email, user.Role, user.MustChangePassword));
    }
}
