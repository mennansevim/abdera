using System.Security.Claims;
using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Features;

// Platform yöneticisi girişi (§3.1, §11): parola + TOTP. TOTP etkinleşmeden yalnızca kurulum uçları
// açıktır; okul/sunucu yönetimi "Operator" politikası (TOTP doğrulanmış oturum) ister.
public static class Auth
{
    public const string OperatorPolicy = "Operator";
    public const string TotpClaim = "totp";
    public const string StampClaim = "stamp";

    public record LoginRequest(string Email, string Password, string? TotpCode);
    public record MeResponse(Guid Id, string Email, bool TotpEnabled, bool TotpVerified);
    public record TotpSetupResponse(string Secret, string ProvisioningUri);
    public record TotpConfirmRequest(string Code);

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("login");
        group.MapPost("/logout", (Delegate)LogoutAsync).AllowAnonymous();
        group.MapGet("/me", MeAsync).RequireAuthorization();
        group.MapPost("/totp/setup", TotpSetupAsync).RequireAuthorization();
        group.MapPost("/totp/confirm", TotpConfirmAsync).RequireAuthorization().RequireRateLimiting("login");
    }

    // İlk açılışta hiç yönetici yoksa Platform:BootstrapEmail/Password ile tek bir hesap açılır.
    public static async Task BootstrapAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (await db.Users.AnyAsync()) return;
        var email = config["Platform:BootstrapEmail"];
        var password = config["Platform:BootstrapPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        if (password.Length < 12) throw new InvalidOperationException("Platform:BootstrapPassword en az 12 karakter olmalı.");

        var now = TimeProvider.System.GetUtcNow();
        var user = PlatformUser.Create(email, now);
        user.SetPasswordHash(new PasswordHasher<PlatformUser>().HashPassword(user, password), now);
        db.Users.Add(user);
        db.AuditLogs.Add(PlatformAuditLog.Record(null, "operator.bootstrapped", null, now, afterJson: JsonSerializer.Serialize(new { user.Email })));
        await db.SaveChangesAsync();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, HttpContext http, PlatformDbContext db, IPasswordHasher<PlatformUser> hasher,
        IDataProtectionVault vault, TimeProvider time)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == request.Email.Trim().ToLower());
        // Kullanıcı yokken de hash doğrulaması yapılır: cevap süresinden hesabın varlığı anlaşılmasın.
        var verification = hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, request.Password ?? "");
        if (user is null || verification == PasswordVerificationResult.Failed)
            return Results.Problem(title: "Giriş başarısız", detail: "E-posta, parola veya kod hatalı.", statusCode: 401);

        var totpVerified = false;
        if (user.TotpEnabled)
        {
            var step = Totp.Verify(vault.Unprotect(user.TotpSecretCiphertext!), request.TotpCode, time.GetUtcNow(), user.LastTotpStep);
            if (step is null) return Results.Problem(title: "Giriş başarısız", detail: "E-posta, parola veya kod hatalı.", statusCode: 401);
            user.AcceptTotpStep(step.Value);
            totpVerified = true;
        }

        db.AuditLogs.Add(PlatformAuditLog.Record(user.Id, "operator.login", null, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { totp = totpVerified })));
        await db.SaveChangesAsync();
        await SignInAsync(http, user, totpVerified);
        return Results.Ok(new MeResponse(user.Id, user.Email, user.TotpEnabled, totpVerified));
    }

    private static async Task SignInAsync(HttpContext http, PlatformUser user, bool totpVerified)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(TotpClaim, totpVerified ? "true" : "false"),
            new Claim(StampClaim, user.SecurityStamp.ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, PlatformDbContext db)
    {
        var user = await db.Users.SingleAsync(u => u.Id == UserId(principal));
        return Results.Ok(new MeResponse(user.Id, user.Email, user.TotpEnabled, principal.FindFirstValue(TotpClaim) == "true"));
    }

    // TOTP etkinse yeniden kurulum yalnızca TOTP doğrulanmış oturumla yapılabilir.
    private static async Task<IResult> TotpSetupAsync(ClaimsPrincipal principal, PlatformDbContext db, IDataProtectionVault vault, TimeProvider time)
    {
        var user = await db.Users.SingleAsync(u => u.Id == UserId(principal));
        if (user.TotpEnabled && principal.FindFirstValue(TotpClaim) != "true")
            return Results.Problem(title: "Yetki yok", detail: "Önce mevcut kodla giriş yapın.", statusCode: 403);
        var secret = Totp.NewSecret();
        user.BeginTotpEnrollment(vault.Protect(secret), time.GetUtcNow());
        await db.SaveChangesAsync();
        return Results.Ok(new TotpSetupResponse(secret, Totp.ProvisioningUri(secret, user.Email)));
    }

    private static async Task<IResult> TotpConfirmAsync(
        TotpConfirmRequest request, HttpContext http, ClaimsPrincipal principal, PlatformDbContext db, IDataProtectionVault vault, TimeProvider time)
    {
        var user = await db.Users.SingleAsync(u => u.Id == UserId(principal));
        if (user.TotpSecretCiphertext is null) return Results.Problem(title: "Kurulum yok", detail: "Önce TOTP kurulumunu başlatın.", statusCode: 409);
        var step = Totp.Verify(vault.Unprotect(user.TotpSecretCiphertext), request.Code, time.GetUtcNow(), user.LastTotpStep);
        if (step is null) throw ValidationFailedException.For("code", "Kod doğrulanamadı.");
        user.EnableTotp(step.Value, time.GetUtcNow());
        db.AuditLogs.Add(PlatformAuditLog.Record(user.Id, "operator.totp_enabled", null, time.GetUtcNow()));
        await db.SaveChangesAsync();
        // Güvenlik damgası değişti: oturum TOTP doğrulanmış olarak yeniden açılır.
        await SignInAsync(http, user, totpVerified: true);
        return Results.Ok(new MeResponse(user.Id, user.Email, true, true));
    }

    public static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static readonly PlatformUser DummyUser = PlatformUser.Create("dummy@invalid", DateTimeOffset.UnixEpoch);
    private static readonly string DummyHash = new PasswordHasher<PlatformUser>().HashPassword(DummyUser, "dummy-password-for-timing");
}

// Yönetici TOTP sırrı için ayrı amaçlı koruyucu (okul sırlarından bağımsız).
public interface IDataProtectionVault
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

public class OperatorSecretProtector(IDataProtectionProvider provider) : IDataProtectionVault
{
    private readonly IDataProtector _protector = provider.CreateProtector("Abdera.Platform.OperatorTotp.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
