using System.Security.Claims;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// docs/10-decisions.md Karar F reversal: veli artık telefon numarası + WhatsApp OTP ile giriş
// yapabiliyor (yalnızca kendi RSVP'sini/takvimini görmek için, bkz. GuardianPortal.cs). Bu,
// Auth/Features/Login.cs'teki e-posta/şifre modelinden tamamen ayrı bir akış - `users`
// tablosunda hiçbir zaman bir Guardian satırı olmaz, oturum doğrudan Guardian.Id + Role=Guardian
// claim'iyle kurulur (bkz. UserRole.cs).
public static class GuardianAuth
{
    public record RequestOtpRequest(string PhoneNumber);
    // DebugCode yalnızca Development ortamında doldurulur - gerçek bir Meta WABA hesabı olmadan
    // uçtan uca test edilebilsin diye (DevWhatsAppSimulator'daki dev-only kısayolla aynı ruh).
    public record RequestOtpResponse(string Message, string? DebugCode);
    public record VerifyOtpRequest(string PhoneNumber, string Code);
    public record VerifyOtpResponse(Guid Id, string FirstName, string LastName);
    public record GuardianMeResponse(Guid Id, string FirstName, string LastName, string PhoneNumber);
    // Karar F (ikinci) reversal: telefon + şifre ile giriş (docs/13-...). Yanıt gövdesi OTP
    // doğrulamasıyla aynı (VerifyOtpResponse) - frontend tek bir başarı şeklini işler.
    public record LoginRequest(string PhoneNumber, string Password);

    private const string OtpTemplateName = "guardian_login_otp";
    private const string GenericFailureDetail = "Telefon numarası veya şifre hatalı.";
    private const string DebugPhoneNumber = "+905550000001";

    // SEC-4 (Login.cs) ile aynı ruh: kayıtsız bir numarada da eş zamanlı bir maliyet oluşturup
    // yanıt süresinin kayıtlı/kayıtsız numara arasında bir numaralandırma kanalı açmasını önler.
    private static readonly Guardian DummyGuardian = Guardian.Create("Dummy", "Guardian", "+905551234567", DateTimeOffset.UnixEpoch);
    // Şifresiz/kayıtsız velide de bir VerifyHashedPassword maliyeti oluşturmak için önceden
    // hesaplanmış bir dummy hash (Login.cs'teki DummyPasswordHash ile aynı desen).
    private static readonly string DummyPasswordHash =
        new PasswordHasher<Guardian>().HashPassword(DummyGuardian, "dummy-password-for-timing-safety-only");

    public static void MapGuardianAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/guardian/login", LoginAsync).AllowAnonymous().RequireRateLimiting("guardian-otp");
        app.MapPost("/api/guardian/otp/request", RequestOtpAsync).AllowAnonymous().RequireRateLimiting("guardian-otp");
        app.MapPost("/api/guardian/otp/verify", VerifyOtpAsync).AllowAnonymous().RequireRateLimiting("guardian-otp");
        app.MapGet("/api/guardian/me", MeAsync).RequireAuthorization(AuthorizationPolicies.GuardianOnly);
        app.MapPost("/api/guardian/logout", GuardianLogoutAsync)
            .AllowAnonymous();

        // Development'ta ve açıkça Demo:Enabled olarak işaretlenmiş staging yayınında
        // gerçek WhatsApp/OTP gerektirmeden örnek veli portalı açılabilir.
        var environment = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var configuration = app.ServiceProvider.GetRequiredService<IConfiguration>();
        if (environment.IsDevelopment() ||
            (environment.IsStaging() && configuration.GetValue<bool>("Demo:Enabled")))
        {
            app.MapPost("/api/guardian/debug-login", DebugLoginAsync).AllowAnonymous();
        }
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, AbderaDbContext db,
        IPasswordHasher<Guardian> passwordHasher, HttpContext httpContext)
    {
        string normalizedPhone;
        try
        {
            normalizedPhone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
        }
        catch (ArgumentException)
        {
            // Zamanlama güvenliği: geçersiz numarada da bir doğrulama maliyeti oluştur.
            passwordHasher.VerifyHashedPassword(DummyGuardian, DummyPasswordHash, request.Password ?? "");
            return Results.Problem(statusCode: 401, title: "Giriş başarısız", detail: GenericFailureDetail);
        }

        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.PhoneNumber == normalizedPhone);
        if (guardian?.PasswordHash is null)
        {
            passwordHasher.VerifyHashedPassword(DummyGuardian, DummyPasswordHash, request.Password ?? "");
            return Results.Problem(statusCode: 401, title: "Giriş başarısız", detail: GenericFailureDetail);
        }

        var verifyResult = passwordHasher.VerifyHashedPassword(guardian, guardian.PasswordHash, request.Password ?? "");
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return Results.Problem(statusCode: 401, title: "Giriş başarısız", detail: GenericFailureDetail);
        }

        await SignInGuardianAsync(guardian, httpContext);
        return Results.Ok(new VerifyOtpResponse(guardian.Id, guardian.FirstName, guardian.LastName));
    }

    private static async Task<IResult> RequestOtpAsync(
        RequestOtpRequest request, AbderaDbContext db, IClock clock, IHostEnvironment env,
        IPasswordHasher<Guardian> passwordHasher, IWhatsAppClient whatsAppClient)
    {
        string normalizedPhone;
        try
        {
            normalizedPhone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["phoneNumber"] = [ex.Message] });
        }

        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.PhoneNumber == normalizedPhone);
        string? debugCode = null;

        if (guardian is not null)
        {
            var code = OtpGenerator.Generate();
            var hash = passwordHasher.HashPassword(guardian, code);
            db.GuardianLoginCodes.Add(GuardianLoginCode.Create(guardian.Id, hash, clock.UtcNow));
            await db.SaveChangesAsync();

            await whatsAppClient.SendTemplateAsync(
                guardian.PhoneNumber, OtpTemplateName, new Dictionary<string, string> { ["code"] = code });

            if (env.IsDevelopment())
            {
                debugCode = code;
            }
        }
        else
        {
            passwordHasher.HashPassword(DummyGuardian, "dummy-otp-for-timing-safety-only");
        }

        return Results.Ok(new RequestOtpResponse("Telefon numaran kayıtlıysa birazdan WhatsApp'tan bir kod alacaksın.", debugCode));
    }

    private static async Task<IResult> VerifyOtpAsync(
        VerifyOtpRequest request, AbderaDbContext db, IClock clock,
        IPasswordHasher<Guardian> passwordHasher, HttpContext httpContext)
    {
        string normalizedPhone;
        try
        {
            normalizedPhone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
        }
        catch (ArgumentException)
        {
            return Results.Problem(statusCode: 401, title: "Giriş başarısız", detail: GenericFailureDetail);
        }

        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.PhoneNumber == normalizedPhone);
        if (guardian is null)
        {
            passwordHasher.VerifyHashedPassword(
                DummyGuardian, passwordHasher.HashPassword(DummyGuardian, "dummy"), request.Code);
            return Results.Problem(statusCode: 401, title: "Giriş başarısız", detail: GenericFailureDetail);
        }

        var candidate = await db.GuardianLoginCodes
            .Where(c => c.GuardianId == guardian.Id)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        if (candidate is null || !candidate.IsUsable(clock.UtcNow))
        {
            return Results.Problem(statusCode: 401, title: "Giriş başarısız", detail: "Kod geçersiz veya süresi dolmuş, yeni bir kod iste.");
        }

        var verifyResult = passwordHasher.VerifyHashedPassword(guardian, candidate.CodeHash, request.Code);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            candidate.RegisterFailedAttempt(clock.UtcNow);
            await db.SaveChangesAsync();
            return Results.Problem(statusCode: 401, title: "Giriş başarısız", detail: "Kod geçersiz veya süresi dolmuş, yeni bir kod iste.");
        }

        candidate.MarkConsumed(clock.UtcNow);
        await db.SaveChangesAsync();

        await SignInGuardianAsync(guardian, httpContext);

        return Results.Ok(new VerifyOtpResponse(guardian.Id, guardian.FirstName, guardian.LastName));
    }

    private static async Task<IResult> DebugLoginAsync(AbderaDbContext db, IClock clock, HttpContext httpContext)
    {
        var guardian = await db.Guardians.SingleOrDefaultAsync(item => item.PhoneNumber == DebugPhoneNumber);
        if (guardian is null)
        {
            guardian = Guardian.Create("Demo", "Veli", DebugPhoneNumber, clock.UtcNow);
            db.Guardians.Add(guardian);
            await db.SaveChangesAsync();
        }

        await SignInGuardianAsync(guardian, httpContext);
        return Results.Ok(new VerifyOtpResponse(guardian.Id, guardian.FirstName, guardian.LastName));
    }

    // Veli çıkışında tarayıcı cookie'sini bekletmeden sil. Genel /api/auth/logout
    // personel hesaplarında güvenlik damgasını da yeniler; veli portalında ise bu hızlı
    // uç kullanıcıyı veritabanı yazma beklemesine sokmadan çıkarır.
    private static async Task GuardianLogoutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static Task SignInGuardianAsync(Guardian guardian, HttpContext httpContext)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, guardian.Id.ToString()),
            new(ClaimTypes.Role, UserRole.Guardian.ToString()),
            new(SecurityStampClaim.ClaimType, guardian.SecurityStamp.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        // Veli portalı çoğunlukla kişisel telefondan kullanılır. Kalıcı cookie sayesinde
        // tarayıcı kapanıp açılsa da aynı veli doğrudan kendi portalına döner; güvenlik damgası
        // kontrolü ve çıkış işlemi kopyalanmış/eski cookie'leri yine geçersiz kılar.
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var clock = httpContext.RequestServices.GetRequiredService<IClock>();
        var rememberedDays = Math.Clamp(configuration.GetValue("Auth:GuardianSessionDays", 30), 1, 365);
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = true,
            ExpiresUtc = clock.UtcNow.AddDays(rememberedDays),
        };
        return httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, AbderaDbContext db)
    {
        var id = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var guardian = await db.Guardians.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id)
            ?? throw new ForbiddenException("Veli kaydı artık mevcut değil.");

        return Results.Ok(new GuardianMeResponse(guardian.Id, guardian.FirstName, guardian.LastName, guardian.PhoneNumber));
    }
}
