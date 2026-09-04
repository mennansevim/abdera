using System.Security.Claims;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

// Oturumdaki personelin (öğretmen/yönetici) kendi ekran içi bildirimleri.
// docs/04-permissions.md "hedef kaynak oturumdan çözümlenir, URL'deki id'ye güvenilmez"
// kuralı burada en katı biçimde geçerli: uçlar hiçbir yerde kullanıcı id'si almaz, her
// sorgu oturumun kendi id'siyle filtrelenir - bir öğretmen başkasının bildirimini ne
// okuyabilir ne de okundu işaretleyebilir.
public static class StaffNotifications
{
    public record StaffNotificationResponse(
        Guid Id,
        StaffNotificationType Type,
        string Title,
        string Body,
        string ReferenceType,
        Guid ReferenceId,
        DateTimeOffset? ReadAt,
        DateTimeOffset CreatedAt);

    public record ListResponse(List<StaffNotificationResponse> Items, int UnreadCount);

    // Zil listesi bir "gelen kutusu" değil, son olayların özeti - sayfalama yerine sabit tavan.
    private const int MaxItems = 30;

    public static void MapStaffNotifications(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/notifications").RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        group.MapGet("", ListAsync);
        group.MapPost("/{notificationId:guid}/read", MarkReadAsync);
        group.MapPost("/read-all", MarkAllReadAsync);
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, AbderaDbContext db)
    {
        var userId = AuthContext.GetUserId(principal);
        var notifications = await db.StaffNotifications
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(MaxItems)
            .ToListAsync();
        var unreadCount = await db.StaffNotifications
            .CountAsync(notification => notification.UserId == userId && notification.ReadAt == null);

        return Results.Ok(new ListResponse(notifications.Select(ToResponse).ToList(), unreadCount));
    }

    private static async Task<IResult> MarkReadAsync(Guid notificationId, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var userId = AuthContext.GetUserId(principal);
        var notification = await db.StaffNotifications
            .SingleOrDefaultAsync(item => item.Id == notificationId && item.UserId == userId)
            ?? throw new NotFoundException("Bildirim bulunamadı.");

        notification.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(notification));
    }

    private static async Task<IResult> MarkAllReadAsync(ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var userId = AuthContext.GetUserId(principal);
        var unread = await db.StaffNotifications
            .Where(notification => notification.UserId == userId && notification.ReadAt == null)
            .ToListAsync();

        foreach (var notification in unread) notification.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync();

        return Results.Ok(new { markedCount = unread.Count });
    }

    private static StaffNotificationResponse ToResponse(StaffNotification notification) => new(
        notification.Id,
        notification.Type,
        notification.Title,
        notification.Body,
        notification.ReferenceType,
        notification.ReferenceId,
        notification.ReadAt,
        notification.CreatedAt);
}
