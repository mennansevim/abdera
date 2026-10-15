using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Auth.Features;

// Kullanıcı yalnızca KENDİ görünen adını değiştirir: hedef id istekte yok, oturumdaki
// kullanıcıdan okunur - başka bir hesabın adını değiştirmenin yolu bu uçta bulunmaz.
// DisplayName null gönderilirse kayıt temizlenir ve yedek ada (öğretmen kaydı/e-posta) dönülür.
public static class UpdateDisplayName
{
    public record Request(string? DisplayName);

    public static void MapUpdateDisplayName(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/auth/me/display-name", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Request request,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw new ForbiddenException("Hesap artık aktif değil.");

        var before = user.DisplayName;
        try
        {
            user.SetDisplayName(request.DisplayName, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["displayName"] = [ex.Message.Split(" (Parameter")[0]],
            });
        }

        if (before != user.DisplayName)
        {
            db.AuditLogs.Add(AuditLog.Record(
                userId, "user.display_name_changed", nameof(User), user.Id, clock.UtcNow,
                beforeJson: JsonSerializer.Serialize(new { displayName = before }),
                afterJson: JsonSerializer.Serialize(new { displayName = user.DisplayName })));
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
