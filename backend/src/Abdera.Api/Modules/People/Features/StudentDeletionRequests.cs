using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Infrastructure;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// Öğretmen kendi öğrencisini ekleyip düzenleyebilir ama silemez; silme yöneticinin
// onayından geçer (docs/10-decisions.md J2). Scheduling'deki "Ders Talepleri" akışının
// aynısı: öğretmen gerekçeli talep açar, yöneticiye ekran içi bildirim düşer, yönetici
// onaylar ya da reddeder.
//
// Onaylandığında silme işi PersonEraser'a devredilir - yöneticinin doğrudan sildiği
// durumla birebir aynı yol, ikinci bir silme mantığı yok.
public static class StudentDeletionRequests
{
    public record CreateRequest(string Reason);
    public record DecisionRequest(string? Note);

    public record RequestResponse(
        Guid Id, Guid StudentId, string StudentName, Guid RequestedBy, string RequestedByName,
        string Reason, StudentDeletionRequestStatus Status, string? DecisionNote,
        DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt,
        // Yönetici neyi onayladığını görmeden karar vermesin - bekleyen taleplerde dolu.
        PersonEraser.StudentImpact? Impact);

    public static void MapStudentDeletionRequests(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/students/{studentId:guid}/deletion-requests", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        // Öğretmen kendi taleplerinin durumunu görebilmeli - "istedim ama ne oldu"
        // sorusunun cevabı yoksa akış yarım kalır.
        app.MapGet("/api/student-deletion-requests", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapPost("/api/student-deletion-requests/{requestId:guid}/approve", ApproveAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        app.MapPost("/api/student-deletion-requests/{requestId:guid}/reject", RejectAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> CreateAsync(
        Guid studentId, CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        await PeopleAuthorization.EnsureStudentAccessAsync(studentId, principal, db);

        if (await db.StudentDeletionRequests.AnyAsync(item =>
                item.StudentId == studentId && item.Status == StudentDeletionRequestStatus.Pending))
        {
            throw new ConflictException("Bu öğrenci için zaten bekleyen bir silme talebi var.");
        }

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var deletionRequest = StudentDeletionRequest.Create(studentId, actorId, request.Reason, now);
        db.StudentDeletionRequests.Add(deletionRequest);

        var student = await db.Students.AsNoTracking().SingleAsync(item => item.Id == studentId);
        var requesterName = await ResolveActorNameAsync(actorId, db);

        // Yöneticilere ekran içi bildirim. Idempotency anahtarı (user, type, reference)
        // olduğu için aynı talep için ikinci bir satır oluşmaz.
        var adminIds = await db.Users
            .Where(user => user.Role == UserRole.Admin && user.IsActive)
            .Select(user => user.Id)
            .ToListAsync();

        foreach (var adminId in adminIds)
        {
            db.StaffNotifications.Add(StaffNotification.Create(
                adminId,
                StaffNotificationType.StudentDeletionRequested,
                "Öğrenci silme talebi",
                $"{requesterName}, {student.FirstName} {student.LastName} adlı öğrencinin silinmesini istedi.",
                nameof(StudentDeletionRequest),
                deletionRequest.Id,
                now));
        }

        db.AuditLogs.Add(AuditLog.Record(
            actorId, "student.deletion_requested", nameof(StudentDeletionRequest), deletionRequest.Id, now,
            afterJson: JsonSerializer.Serialize(new { studentId, reason = deletionRequest.Reason })));

        await db.SaveChangesAsync();
        return Results.Created(
            $"/api/student-deletion-requests/{deletionRequest.Id}",
            await ToResponseAsync(deletionRequest, db, includeImpact: false));
    }

    private static async Task<IResult> ListAsync(
        StudentDeletionRequestStatus? status, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        var query = db.StudentDeletionRequests.AsNoTracking().AsQueryable();

        if (status is { } requested) query = query.Where(item => item.Status == requested);

        if (teacherId is { } scopedTeacherId)
        {
            // Öğretmen yalnızca kendi öğrencileri için açılmış talepleri görür.
            var ownStudentIds = await db.Enrollments
                .Where(enrollment => enrollment.TeacherId == scopedTeacherId)
                .Select(enrollment => enrollment.StudentId)
                .Distinct()
                .ToListAsync();
            query = query.Where(item => ownStudentIds.Contains(item.StudentId));
        }

        var requests = await query.OrderByDescending(item => item.CreatedAt).ToListAsync();

        var responses = new List<RequestResponse>(requests.Count);
        foreach (var item in requests)
        {
            // Etki dökümü yalnızca bekleyen taleplerde ve yalnızca yönetici için hesaplanır -
            // karar vermesi gereken kişi o. Her satır için birkaç sorgu demek, ama bekleyen
            // talep sayısı bu ölçekte tek haneli.
            var includeImpact = teacherId is null && item.Status == StudentDeletionRequestStatus.Pending;
            responses.Add(await ToResponseAsync(item, db, includeImpact));
        }

        return Results.Ok(responses);
    }

    private static async Task<IResult> ApproveAsync(
        Guid requestId, DecisionRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var deletionRequest = await db.StudentDeletionRequests.SingleOrDefaultAsync(item => item.Id == requestId)
            ?? throw new NotFoundException("Silme talebi bulunamadı.");

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        deletionRequest.Approve(actorId, request.Note, now);

        var impact = await PersonEraser.DescribeStudentAsync(deletionRequest.StudentId, db);

        // Onay = açık karar, bu yüzden tahsilat geçmişi olsa bile silme uygulanır.
        // Kalıcı iz burada: talep satırının kendisi öğrenciyle birlikte cascade ile
        // gideceği için (student_deletion_requests.student_id FK), kararın kaydı
        // audit_log'da tutulur - audit hiçbir zaman temizlenmez.
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "student.deletion_request_approved", nameof(Student), deletionRequest.StudentId, now,
            beforeJson: JsonSerializer.Serialize(impact),
            afterJson: JsonSerializer.Serialize(new
            {
                requestId,
                requestedBy = deletionRequest.RequestedBy,
                reason = deletionRequest.Reason,
                decisionNote = deletionRequest.DecisionNote,
            })));

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await PersonEraser.EraseStudentAsync(deletionRequest.StudentId, db);
        await transaction.CommitAsync();

        return Results.Ok(impact);
    }

    private static async Task<IResult> RejectAsync(
        Guid requestId, DecisionRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var deletionRequest = await db.StudentDeletionRequests.SingleOrDefaultAsync(item => item.Id == requestId)
            ?? throw new NotFoundException("Silme talebi bulunamadı.");

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        deletionRequest.Reject(actorId, request.Note, now);

        db.AuditLogs.Add(AuditLog.Record(
            actorId, "student.deletion_request_rejected", nameof(StudentDeletionRequest), requestId, now,
            afterJson: JsonSerializer.Serialize(new { deletionRequest.StudentId, deletionRequest.DecisionNote })));

        await db.SaveChangesAsync();
        return Results.Ok(await ToResponseAsync(deletionRequest, db, includeImpact: false));
    }

    private static async Task<RequestResponse> ToResponseAsync(
        StudentDeletionRequest request, AbderaDbContext db, bool includeImpact)
    {
        var student = await db.Students.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.StudentId);

        return new RequestResponse(
            request.Id,
            request.StudentId,
            student is null ? "Silinmiş öğrenci" : $"{student.FirstName} {student.LastName}",
            request.RequestedBy,
            await ResolveActorNameAsync(request.RequestedBy, db),
            request.Reason,
            request.Status,
            request.DecisionNote,
            request.CreatedAt,
            request.ResolvedAt,
            includeImpact ? await PersonEraser.DescribeStudentAsync(request.StudentId, db) : null);
    }

    // Talebi açanın görünen adı: öğretmen kaydı varsa ad-soyad, yoksa e-posta.
    private static async Task<string> ResolveActorNameAsync(Guid userId, AbderaDbContext db)
    {
        var teacher = await db.Teachers.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId);
        if (teacher is not null) return $"{teacher.FirstName} {teacher.LastName}";

        var email = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.Email)
            .SingleOrDefaultAsync();
        return email ?? "Bilinmeyen kullanıcı";
    }
}
