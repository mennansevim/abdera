using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Auth.Features;

// Yerel geliştirmede şifre sormadan personel oturumu açar: giriş ekranında listeden seçilen
// hesapla (e-posta verilmezse seçili rolün ilk hesabıyla) girilir. Yalnızca
// Development ortamında VE Auth:DevLogin:Enabled=true iken haritalanır (AuthModule).
// Tek başına ortam adı yetmez: .env.example varsayılanı Development olduğu için şablondan
// kurulan bir sunucu bu yolu internete açmasın diye ikinci, açık bir onay gerekiyor.
public static class DevLogin
{
    public record Request(UserRole Role, string? Email = null);

    // Giriş ekranındaki yerel hesap seçicisi için: şifre yerine listeden hesap seçilir.
    public record Account(string Email, UserRole Role, string? Name);

    public static void MapDevLogin(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dev/auth/accounts", ListAccountsAsync).AllowAnonymous();
        app.MapPost("/api/dev/auth/login", HandleAsync).AllowAnonymous();
    }

    private static async Task<IResult> ListAccountsAsync(AbderaDbContext db)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role != UserRole.Guardian)
            .Select(u => new { u.Id, u.Email, u.Role })
            .ToListAsync();
        // Öğretmen adı People modülündedir; Me.cs'teki gibi açık bir sorguyla okunur.
        var teacherNames = await db.Teachers.AsNoTracking()
            .Where(t => t.UserId != null)
            .Select(t => new { UserId = t.UserId!.Value, Name = t.FirstName + " " + t.LastName })
            .ToDictionaryAsync(t => t.UserId, t => t.Name);

        var accounts = users
            .Select(u => new Account(u.Email, u.Role, teacherNames.GetValueOrDefault(u.Id)))
            .OrderBy(a => a.Role).ThenBy(a => a.Name ?? a.Email, StringComparer.CurrentCulture)
            .ToList();
        return Results.Ok(accounts);
    }

    private static async Task<IResult> HandleAsync(
        Request request,
        AbderaDbContext db,
        HttpContext httpContext,
        ILogger<Program> logger)
    {
        // Veli oturumu users tablosunda değil, Guardian.Id üzerinden kurulur (UserRole yorumu).
        if (request.Role == UserRole.Guardian)
        {
            return Results.Problem(statusCode: 400, title: "Desteklenmiyor", detail: "Yerel hızlı giriş yalnızca yönetici ve öğretmen içindir.");
        }

        var email = request.Email?.Trim().ToLowerInvariant();
        var user = string.IsNullOrEmpty(email)
            ? await db.Users.Where(u => u.Role == request.Role && u.IsActive).OrderBy(u => u.CreatedAt).FirstOrDefaultAsync()
            : await db.Users.SingleOrDefaultAsync(u => u.Email == email && u.IsActive);

        if (user is null)
        {
            return Results.Problem(statusCode: 404, title: "Hesap bulunamadı",
                detail: string.IsNullOrEmpty(email) ? "Bu rolde aktif bir hesap yok." : "Bu e-postayla aktif bir hesap yok.");
        }

        // Şifreli girişteki rol bağlayıcılığı burada da geçerli: "Yöneticiyim" seçip bir
        // öğretmen e-postası yazmak öğretmen oturumu açmaz.
        if (user.Role != request.Role)
        {
            return Results.Problem(statusCode: 403, title: "Rol uyuşmuyor",
                detail: $"Bu hesap {Login.RoleLabel(user.Role)} hesabı. Lütfen \"{Login.RoleChoiceLabel(user.Role)}\" seçeneğiyle giriş yap.");
        }

        await Login.SignInAsync(httpContext, user);
        logger.LogWarning("Yerel hızlı giriş (şifresiz) kullanıldı: kullanıcı={UserId} rol={Role}", user.Id, user.Role);

        // Şifre değiştirme zorunluluğu şifreli girişe aittir; yerelde her girişte ayarlara
        // yönlendirmesin. Hesaptaki bayrak değişmez, gerçek girişte yine sorulur.
        return Results.Ok(new Login.Response(user.Id, user.Email, user.Role, MustChangePassword: false));
    }
}
