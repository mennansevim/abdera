using System.Security.Claims;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Auth.Features;

// docs/10-decisions.md B4: MVP'de e-posta kanalı yok, bu yüzden "şifremi unuttum" akışı
// yerine kullanıcı mevcut (geçici veya normal) şifresini bilerek kendi şifresini değiştirir.
public static class ChangePassword
{
    public record Request(string CurrentPassword, string NewPassword);

    public static void MapChangePassword(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/change-password", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Request request,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IPasswordHasher<User> passwordHasher,
        IClock clock)
    {
        if (request.NewPassword.Length < 8)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["newPassword"] = ["Yeni şifre en az 8 karakter olmalı."],
            });
        }

        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.SingleAsync(u => u.Id == userId);

        var verifyResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["currentPassword"] = ["Mevcut şifre hatalı."],
            });
        }

        var newHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.SetPassword(newHash, clock.UtcNow, mustChangePassword: false);

        db.AuditLogs.Add(AuditLog.Record(userId, "user.password_changed", nameof(User), user.Id, clock.UtcNow));

        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
