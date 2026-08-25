using System.Security.Claims;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Auth.Features;

public static class Me
{
    // AiRewriteAvailable: Faz 10'daki "yapıcı metne dönüştür" özelliği yapılandırılmış mı?
    // Frontend butonu buna göre açar/kapatır - kullanıcı çalışmayan bir düğmeye basmasın.
    // Ayrı bir /api/capabilities ucu açmak yerine buraya eklendi: istemci zaten her açılışta
    // bu yanıtı okuyor, ikinci bir istek gereksiz olurdu.
    public record Response(Guid Id, string Email, UserRole Role, bool MustChangePassword, bool AiRewriteAvailable);

    public static void MapMe(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/me", HandleAsync).RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(ClaimsPrincipal principal, AbderaDbContext db, IConstructiveTextRewriter rewriter)
    {
        var id = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Claim'ler yalnızca giriş anındaki bilgiyi taşır; MustChangePassword aynı oturum
        // içinde ChangePassword ile değişebileceğinden veritabanından taze okunur.
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.IsActive)
            ?? throw new ForbiddenException("Hesap artık aktif değil.");

        return Results.Ok(new Response(user.Id, user.Email, user.Role, user.MustChangePassword, rewriter.IsAvailable));
    }
}
