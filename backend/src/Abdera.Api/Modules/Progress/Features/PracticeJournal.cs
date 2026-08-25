using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Features;

public static class PracticeJournal
{
    public record Request(DateOnly Date, int DurationMinutes, string Goal, string? Note);
    public record EntryResponse(
        Guid Id, Guid StudentId, DateOnly Date, int DurationMinutes, string Goal, string? Note,
        bool ParentApproved, DateTimeOffset CreatedAt);
    public record JournalResponse(List<EntryResponse> Entries, int TotalMinutes, List<string> Badges);

    public static void MapPracticeJournal(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/students/{studentId:guid}/practice-journal", StaffListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/students/{studentId:guid}/practice-journal", StaffCreateAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapGet("/api/guardian/me/students/{studentId:guid}/practice-journal", GuardianListAsync)
            .RequireAuthorization(AuthorizationPolicies.GuardianOnly);
        app.MapPost("/api/guardian/me/students/{studentId:guid}/practice-journal", GuardianCreateAsync)
            .RequireAuthorization(AuthorizationPolicies.GuardianOnly);
        app.MapPost("/api/guardian/me/practice-journal/{entryId:guid}/approve", GuardianApproveAsync)
            .RequireAuthorization(AuthorizationPolicies.GuardianOnly);
    }

    private static async Task<IResult> StaffListAsync(
        Guid studentId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        await ProgressAuthorization.EnsureStudentAccessAsync(studentId, principal, db);
        return Results.Ok(await BuildResponseAsync(studentId, db));
    }

    private static async Task<IResult> StaffCreateAsync(
        Guid studentId, Request request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        await ProgressAuthorization.EnsureStudentAccessAsync(studentId, principal, db);
        return await CreateAsync(studentId, request, principal, db, clock, approveAsGuardian: false);
    }

    private static async Task<IResult> GuardianListAsync(
        Guid studentId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        await EnsureGuardianLinkAsync(studentId, AuthContext.GetUserId(principal), db);
        return Results.Ok(await BuildResponseAsync(studentId, db));
    }

    private static async Task<IResult> GuardianCreateAsync(
        Guid studentId, Request request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var guardianId = AuthContext.GetUserId(principal);
        await EnsureGuardianLinkAsync(studentId, guardianId, db);
        return await CreateAsync(studentId, request, principal, db, clock, approveAsGuardian: true);
    }

    private static async Task<IResult> CreateAsync(
        Guid studentId, Request request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock, bool approveAsGuardian)
    {
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).DateTime);
        if (request.Date > today)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["date"] = ["Çalışma tarihi gelecekte olamaz."] });

        var actorId = AuthContext.GetUserId(principal);
        var entry = PracticeJournalEntry.Create(
            studentId, request.Date, request.DurationMinutes, request.Goal, request.Note, actorId, clock.UtcNow);
        if (approveAsGuardian) entry.Approve(actorId, clock.UtcNow);
        db.PracticeJournalEntries.Add(entry);
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "practice_journal.created", nameof(PracticeJournalEntry), entry.Id, clock.UtcNow,
            null, JsonSerializer.Serialize(new { entry.StudentId, entry.PracticeDate, entry.DurationMinutes, HasNote = entry.Note is not null, ParentApproved = entry.ParentApprovedAt is not null })));
        await db.SaveChangesAsync();
        return Results.Created($"/api/practice-journal/{entry.Id}", ToResponse(entry));
    }

    private static async Task<IResult> GuardianApproveAsync(
        Guid entryId, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var entry = await db.PracticeJournalEntries.SingleOrDefaultAsync(item => item.Id == entryId)
            ?? throw new NotFoundException("Çalışma günlüğü kaydı bulunamadı.");
        var guardianId = AuthContext.GetUserId(principal);
        await EnsureGuardianLinkAsync(entry.StudentId, guardianId, db);
        entry.Approve(guardianId, clock.UtcNow);
        db.AuditLogs.Add(AuditLog.Record(
            guardianId, "practice_journal.parent_approved", nameof(PracticeJournalEntry), entry.Id, clock.UtcNow));
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(entry));
    }

    private static async Task EnsureGuardianLinkAsync(Guid studentId, Guid guardianId, AbderaDbContext db)
    {
        if (!await db.StudentGuardians.AnyAsync(link => link.StudentId == studentId && link.GuardianId == guardianId))
            throw new ForbiddenException("Bu öğrencinin çalışma günlüğüne erişemezsiniz.");
    }

    private static async Task<JournalResponse> BuildResponseAsync(Guid studentId, AbderaDbContext db)
    {
        var entries = await db.PracticeJournalEntries
            .Where(entry => entry.StudentId == studentId)
            .OrderByDescending(entry => entry.PracticeDate)
            .ThenByDescending(entry => entry.CreatedAt)
            .Take(100)
            .ToListAsync();
        var total = entries.Sum(entry => entry.DurationMinutes);
        var badges = new List<string>();
        if (entries.Count > 0) badges.Add("İlk adım");
        if (entries.Count >= 3) badges.Add("Düzenli çalışma");
        if (total >= 120) badges.Add("120 dakika");
        return new JournalResponse(entries.Select(ToResponse).ToList(), total, badges);
    }

    private static EntryResponse ToResponse(PracticeJournalEntry entry) => new(
        entry.Id, entry.StudentId, entry.PracticeDate, entry.DurationMinutes, entry.Goal, entry.Note,
        entry.ParentApprovedAt is not null, entry.CreatedAt);
}
