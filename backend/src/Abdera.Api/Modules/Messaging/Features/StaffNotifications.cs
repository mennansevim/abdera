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
        var recent = await db.StaffNotifications
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(MaxItems)
            .ToListAsync();
        // Cevapsız yoklama soruları tavana takılmadan HER ZAMAN listede: "seçim yapılmayanlar
        // bildirimlerde kalsın" - yeni olaylar onları 30'luk pencerenin dışına itmemeli.
        var openQuestions = await db.StaffNotifications
            .Where(notification => notification.UserId == userId &&
                                   notification.Type == StaffNotificationType.AttendanceMissing &&
                                   notification.ReadAt == null)
            .ToListAsync();
        var notifications = recent
            .UnionBy(openQuestions, notification => notification.Id)
            .OrderByDescending(notification => notification.CreatedAt);
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

        // Yoklama sorusu okumakla kapanmaz, cevaplanınca kapanır (AttendanceReminderJob).
        // Hata dönülmez: ekrandaki açılır kartı kapatmak bu ucu çağırıyor, soru zilde kalır.
        if (notification.Type != StaffNotificationType.AttendanceMissing)
            notification.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(notification));
    }

    private static async Task<IResult> MarkAllReadAsync(ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var userId = AuthContext.GetUserId(principal);
        var unread = await db.StaffNotifications
            .Where(notification => notification.UserId == userId && notification.ReadAt == null)
            .Where(notification => notification.Type != StaffNotificationType.AttendanceMissing)
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
