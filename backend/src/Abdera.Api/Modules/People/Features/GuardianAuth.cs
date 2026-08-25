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

    private const string OtpTemplateName = "guardian_login_otp";
    private const string GenericFailureDetail = "Telefon numarası veya kod hatalı.";
    private const string DebugPhoneNumber = "+905550000001";

    // SEC-4 (Login.cs) ile aynı ruh: kayıtsız bir numarada da eş zamanlı bir maliyet oluşturup
    // yanıt süresinin kayıtlı/kayıtsız numara arasında bir numaralandırma kanalı açmasını önler.
    private static readonly Guardian DummyGuardian = Guardian.Create("Dummy", "Guardian", "+905551234567", DateTimeOffset.UnixEpoch);

    public static void MapGuardianAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/guardian/otp/request", RequestOtpAsync).AllowAnonymous().RequireRateLimiting("guardian-otp");
        app.MapPost("/api/guardian/otp/verify", VerifyOtpAsync).AllowAnonymous().RequireRateLimiting("guardian-otp");
        app.MapGet("/api/guardian/me", MeAsync).RequireAuthorization(AuthorizationPolicies.GuardianOnly);

        // Test kolaylığı için demo veli girişi yalnızca Development'ta route edilir.
        if (app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            app.MapPost("/api/guardian/debug-login", DebugLoginAsync).AllowAnonymous();
        }
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

    private static Task SignInGuardianAsync(Guardian guardian, HttpContext httpContext)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, guardian.Id.ToString()),
            new(ClaimTypes.Role, UserRole.Guardian.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, AbderaDbContext db)
    {
        var id = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var guardian = await db.Guardians.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id)
            ?? throw new ForbiddenException("Veli kaydı artık mevcut değil.");

        return Results.Ok(new GuardianMeResponse(guardian.Id, guardian.FirstName, guardian.LastName, guardian.PhoneNumber));
    }
}
