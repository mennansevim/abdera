using System.Security.Claims;
using System.Security.Cryptography;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Auth.Features;

// docs/10-decisions.md B4: e-posta kanalı yoksa öğretmen şifresini nasıl sıfırlar?
// Yönetici geçici bir şifre üretir, öğretmene sözlü/WhatsApp iletir; öğretmen ilk
// girişte ChangePassword ile kalıcı şifresini belirler (MustChangePassword=true).
public static class ResetPassword
{
    public record Response(string TemporaryPassword);

    public static void MapResetPassword(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/users/{userId:guid}/reset-password", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> HandleAsync(
        Guid userId,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IPasswordHasher<User> passwordHasher,
        IClock clock)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId)
            ?? throw new NotFoundException("Kullanıcı bulunamadı.");

        var temporaryPassword = GenerateTemporaryPassword();
        var hash = passwordHasher.HashPassword(user, temporaryPassword);
        user.SetPassword(hash, clock.UtcNow, mustChangePassword: true);

        var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        db.AuditLogs.Add(AuditLog.Record(actorId, "user.password_reset_by_admin", nameof(User), user.Id, clock.UtcNow));

        await db.SaveChangesAsync();
        return Results.Ok(new Response(temporaryPassword));
    }

    private static string GenerateTemporaryPassword()
    {
        // Okunması kolay, karışıklık yaratan karakterler (0/O, 1/l) hariç - telefonla iletilecek.
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[12];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        }
        return new string(chars);
    }
}
