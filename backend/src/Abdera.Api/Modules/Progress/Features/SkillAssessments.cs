using System.Security.Claims;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Features;

public static class SkillAssessments
{
    public record SkillDefinitionResponse(Guid Id, string Code, string Label, Guid? InstrumentId);
    public record CreateRequest(Guid SkillDefinitionId, Guid? LessonId, int Score, string? Note);
    public record AssessmentResponse(
        Guid Id,
        Guid StudentId,
        Guid SkillDefinitionId,
        string SkillCode,
        string SkillLabel,
        Guid TeacherId,
        Guid? LessonId,
        int Score,
        string? Note,
        DateTimeOffset AssessedAt);

    public static void MapSkillAssessments(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/skill-definitions", ListDefinitionsAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapGet("/api/students/{studentId:guid}/skill-assessments", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/students/{studentId:guid}/skill-assessments", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> ListDefinitionsAsync(Guid? instrumentId, AbderaDbContext db)
    {
        var query = db.SkillDefinitions.AsNoTracking();
        if (instrumentId is { } selectedInstrumentId)
        {
            if (!await db.Instruments.AnyAsync(instrument => instrument.Id == selectedInstrumentId))
                throw new NotFoundException("Enstrüman bulunamadı.");
            query = query.Where(skill => skill.InstrumentId == null || skill.InstrumentId == selectedInstrumentId);
        }

        var definitions = await query
            .OrderBy(skill => skill.InstrumentId == null ? 0 : 1)
            .ThenBy(skill => skill.Label)
            .Select(skill => new SkillDefinitionResponse(skill.Id, skill.Code, skill.Label, skill.InstrumentId))
            .ToListAsync();
        return Results.Ok(definitions);
    }

    private static async Task<IResult> ListAsync(
        Guid studentId,
        ClaimsPrincipal principal,
        AbderaDbContext db)
    {
        await ProgressAuthorization.EnsureStudentAccessAsync(studentId, principal, db);

        var assessments = await (
            from assessment in db.SkillAssessments
            join skill in db.SkillDefinitions on assessment.SkillDefinitionId equals skill.Id
            where assessment.StudentId == studentId
            orderby assessment.AssessedAt descending
            select new AssessmentResponse(
                assessment.Id,
                assessment.StudentId,
                assessment.SkillDefinitionId,
                skill.Code,
                skill.Label,
                assessment.TeacherId,
                assessment.LessonId,
                assessment.Score,
                assessment.Note,
                assessment.AssessedAt)
        ).ToListAsync();
        return Results.Ok(assessments);
    }

    private static async Task<IResult> CreateAsync(
        Guid studentId,
        CreateRequest request,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock)
    {
        if (AuthContext.IsAdmin(principal))
            throw new ForbiddenException("Yetenek değerlendirmesini yalnızca öğretmen girebilir.");
        if (request.Score is < 1 or > 5)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                [nameof(request.Score)] = ["Yetenek puanı 1 ile 5 arasında olmalı."],
            });
        }

        var teacherId = await ProgressAuthorization.EnsureStudentAccessAsync(studentId, principal, db)
            ?? throw new ForbiddenException("Yetenek değerlendirmesini yalnızca öğretmen girebilir.");
        var skill = await db.SkillDefinitions.SingleOrDefaultAsync(item => item.Id == request.SkillDefinitionId)
            ?? throw new NotFoundException("Yetenek tanımı bulunamadı.");

        var enrollmentMatchesSkill = skill.InstrumentId is null || await db.Enrollments.AnyAsync(enrollment =>
            enrollment.StudentId == studentId && enrollment.TeacherId == teacherId &&
            enrollment.InstrumentId == skill.InstrumentId);
        if (!enrollmentMatchesSkill)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                [nameof(request.SkillDefinitionId)] = ["Bu yetenek öğrencinin bu öğretmenle çalıştığı enstrümana ait değil."],
            });
        }

        if (request.LessonId is { } lessonId)
        {
            var lesson = await db.Lessons.SingleOrDefaultAsync(item => item.Id == lessonId)
                ?? throw new NotFoundException("Ders bulunamadı.");
            if (lesson.StudentId != studentId || lesson.TeacherId != teacherId)
                throw new ForbiddenException("Değerlendirme yalnızca bu öğrenciye ait kendi dersinize bağlanabilir.");
            if (skill.InstrumentId is { } instrumentId && lesson.InstrumentId != instrumentId)
            {
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    [nameof(request.SkillDefinitionId)] = ["Yetenek tanımı dersin enstrümanıyla uyuşmuyor."],
                });
            }
        }

        var assessment = SkillAssessment.Create(
            studentId,
            skill.Id,
            teacherId,
            request.LessonId,
            request.Score,
            request.Note,
            clock.UtcNow);
        db.SkillAssessments.Add(assessment);
        await db.SaveChangesAsync();

        return Results.Created(
            $"/api/students/{studentId}/skill-assessments/{assessment.Id}",
            new AssessmentResponse(
                assessment.Id,
                assessment.StudentId,
                assessment.SkillDefinitionId,
                skill.Code,
                skill.Label,
                assessment.TeacherId,
                assessment.LessonId,
                assessment.Score,
                assessment.Note,
                assessment.AssessedAt));
    }
}
