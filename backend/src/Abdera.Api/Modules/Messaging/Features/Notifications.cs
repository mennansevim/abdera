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
        DateTimeOffset ScheduledAt, NotificationJobStatus Status, int AttemptCount, string? LastError, DateTimeOffset? SentAt,
        string? GuardianName, string? StudentName, string? LessonType);

    private record LessonDisplay(Guid LessonId, string StudentName, string LessonType);

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
        // Sıra "ne oldu, sırada ne var" sorusunu yanıtlar (kullanıcı geri bildirimi: en son
        // gönderilen mesaj en üstte görünmeliydi; eski `scheduled_at DESC` sırası aylar sonrasına
        // planlanmış iptal satırlarını başa koyuyordu):
        //   1. Gerçekleşenler (gönderildi / başarısız / işleniyor) - en son olan en üstte.
        //   2. Bekleyenler - en yakın planlanan önce.
        //   3. İptal edilenler - en sonda.
        var jobs = await query
            .OrderBy(j =>
                j.Status == NotificationJobStatus.Sent || j.Status == NotificationJobStatus.Failed || j.Status == NotificationJobStatus.Processing ? 0
                : j.Status == NotificationJobStatus.Pending ? 1
                : 2)
            .ThenByDescending(j => j.Status == NotificationJobStatus.Pending ? (DateTimeOffset?)null : j.SentAt ?? j.UpdatedAt)
            .ThenBy(j => j.ScheduledAt)
            .ThenBy(j => j.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        var lessonIds = jobs.Where(j => j.ReferenceType == "lesson").Select(j => j.ReferenceId).Distinct().ToList();
        var lessonDisplays = await db.Lessons
            .Where(l => lessonIds.Contains(l.Id))
            .Join(db.Students, lesson => lesson.StudentId, student => student.Id, (lesson, student) => new { lesson.Id, lesson.StudentId, StudentName = student.FirstName + " " + student.LastName, lesson.InstrumentId })
            .Join(db.Instruments, row => row.InstrumentId, instrument => instrument.Id, (row, instrument) => new LessonDisplay(row.Id, row.StudentName, instrument.Name))
            .ToDictionaryAsync(row => row.LessonId);

        var recipientPhones = jobs.Select(j => j.RecipientPhoneNumber).Distinct().ToList();
        var guardianNames = await db.Guardians
            .Where(g => recipientPhones.Contains(g.PhoneNumber))
            .ToDictionaryAsync(g => g.PhoneNumber, g => g.FirstName + " " + g.LastName);

        return Results.Ok(new PagedResponse<NotificationJobResponse>(
            jobs.Select(job =>
            {
                lessonDisplays.TryGetValue(job.ReferenceId, out var lesson);
                guardianNames.TryGetValue(job.RecipientPhoneNumber, out var guardianName);
                return ToResponse(job, guardianName, lesson?.StudentName, lesson?.LessonType);
            }).ToList(), totalCount, normalizedPage, normalizedPageSize));
    }

    private static async Task<IResult> RetryAsync(Guid jobId, AbderaDbContext db, IClock clock)
    {
        var job = await db.NotificationJobs.SingleOrDefaultAsync(j => j.Id == jobId)
            ?? throw new NotFoundException("Bildirim job'ı bulunamadı.");

        job.RetryManually(clock.UtcNow);
        await db.SaveChangesAsync();

        return Results.Ok(ToResponse(job, null, null, null));
    }

    private static NotificationJobResponse ToResponse(NotificationJob job, string? guardianName, string? studentName, string? lessonType) => new(
        job.Id, job.Type, job.RecipientPhoneNumber, job.ReferenceType, job.ReferenceId,
        job.ScheduledAt, job.Status, job.AttemptCount, job.LastError, job.SentAt, guardianName, studentName, lessonType);
}
