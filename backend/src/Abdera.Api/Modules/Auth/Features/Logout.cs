using System.Security.Claims;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Auth.Features;

public static class Logout
{
    public static void MapLogout(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/logout", HandleAsync).RequireAuthorization();
    }

    // Daha önce yalnızca SignOutAsync çağrılıyordu - bu istemci cookie'sini siler ama
    // ASP.NET Core'un imzalı ticket'ı kendiliğinden geçersiz kılmaz; aynı eski cookie
    // (örn. bir kopyası alınmışsa) 12 saatlik sliding pencere boyunca sunucuda hâlâ geçerli
    // kalıyordu. Canlı QA turunda bulunan gerçek bug - şimdi SecurityStamp'i yenileyip
    // Program.cs'teki OnValidatePrincipal'ın bir sonraki istekte bu cookie'yi reddetmesini
    // sağlıyoruz.
    private static async Task<IResult> HandleAsync(ClaimsPrincipal principal, AbderaDbContext db, IClock clock, HttpContext httpContext)
    {
        var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var roleClaim = principal.FindFirstValue(ClaimTypes.Role);
        if (idClaim is not null && Guid.TryParse(idClaim, out var id))
        {
            if (roleClaim == UserRole.Guardian.ToString())
            {
                var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.Id == id);
                guardian?.InvalidateSessions(clock.UtcNow);
            }
            else
            {
                var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id);
                user?.InvalidateSessions(clock.UtcNow);
            }

            await db.SaveChangesAsync();
        }

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }
}
