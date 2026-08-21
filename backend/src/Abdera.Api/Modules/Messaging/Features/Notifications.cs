using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

// docs/07-api.md GET/POST /api/notifications - admin panelinde bildirim durumu listesi ve
// FAILED job'lar için "yeniden dene" (abdera-notification skill madde 10).
public static class Notifications
{
    public record NotificationJobResponse(
        Guid Id, NotificationJobType Type, string RecipientPhoneNumber, string ReferenceType, Guid ReferenceId,
        DateTimeOffset ScheduledAt, NotificationJobStatus Status, int AttemptCount, string? LastError, DateTimeOffset? SentAt);

    public static void MapNotifications(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", ListAsync);
        group.MapPost("/{jobId:guid}/retry", RetryAsync);
    }

    private static async Task<IResult> ListAsync(NotificationJobStatus? status, int? page, int? pageSize, AbderaDbContext db)
    {
        var (normalizedPage, normalizedPageSize) = Pagination.Normalize(page, pageSize);

        var query = db.NotificationJobs.AsQueryable();
        if (status is { } s) query = query.Where(j => j.Status == s);

        var totalCount = await query.CountAsync();
        var jobs = await query
            .OrderByDescending(j => j.ScheduledAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        return Results.Ok(new PagedResponse<NotificationJobResponse>(
            jobs.Select(ToResponse).ToList(), totalCount, normalizedPage, normalizedPageSize));
    }

    private static async Task<IResult> RetryAsync(Guid jobId, AbderaDbContext db, IClock clock)
    {
        var job = await db.NotificationJobs.SingleOrDefaultAsync(j => j.Id == jobId)
            ?? throw new NotFoundException("Bildirim job'ı bulunamadı.");

        job.RetryManually(clock.UtcNow);
        await db.SaveChangesAsync();

        return Results.Ok(ToResponse(job));
    }

    private static NotificationJobResponse ToResponse(NotificationJob job) => new(
        job.Id, job.Type, job.RecipientPhoneNumber, job.ReferenceType, job.ReferenceId,
        job.ScheduledAt, job.Status, job.AttemptCount, job.LastError, job.SentAt);
}
