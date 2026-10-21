using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Auth.Features;

// Okulun kullanıcı paketi (docs/multi-tenant/ARCHITECTURE.md §5.7): platform okulun satın aldığı
// personel hesabı sayısını School:StaffSeatLimit olarak yazar. Sayılan hesap etkin yönetici + öğretmen
// hesabıdır; veli ve öğrenci sayılmaz. Değer boşsa okul sınırsızdır (bugünkü davranış).
// Sınır yalnızca YENİ hesap açmayı ya da pasif hesabı geri açmayı durdurur; paket küçültülse bile
// var olan hesaplar kapanmaz. Ayar her istekte IConfiguration'dan okunur (EnabledModules ile aynı neden).
public sealed class StaffSeats(IConfiguration configuration)
{
    public const string ConfigurationKey = "School:StaffSeatLimit";

    public record Response(int? Limit, int Used);

    public int? Limit => int.TryParse(configuration[ConfigurationKey], out var limit) && limit > 0 ? limit : null;

    public static Task<int> CountUsedAsync(AbderaDbContext db) =>
        db.Users.CountAsync(user => user.IsActive && (user.Role == UserRole.Admin || user.Role == UserRole.Teacher));

    public async Task EnsureAvailableAsync(AbderaDbContext db)
    {
        if (Limit is not { } limit) return;
        var used = await CountUsedAsync(db);
        if (used >= limit)
            throw new ConflictException(
                $"Kullanıcı paketiniz dolu ({used}/{limit} personel hesabı). Yeni hesap açmak için paketinizi büyütün ya da kullanılmayan bir öğretmeni pasife alın.");
    }
}

// Öğretmenler ekranının "kullanıcı: 5/8" göstergesi ve dolu paket uyarısı.
public static class StaffSeatEndpoints
{
    public static void MapStaffSeats(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/staff-seats", async (AbderaDbContext db, StaffSeats seats) =>
                Results.Ok(new StaffSeats.Response(seats.Limit, await StaffSeats.CountUsedAsync(db))))
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }
}
