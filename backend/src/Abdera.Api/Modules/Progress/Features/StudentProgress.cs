using System.Security.Claims;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Features;

// Öğrenci gelişim ekranının tek veri kaynağı. Notlar ders bazında yazılmaya devam eder;
// bu uç nokta onları öğrenci, öğretmen ve enstrüman bilgileriyle birlikte kümülatif bir
// zaman akışına dönüştürür.
public static class StudentProgress
{
    public record ProgressEntryResponse(
        Guid Id,
        Guid LessonId,
        Guid TeacherId,
        Guid InstrumentId,
        DateTimeOffset LessonStartAt,
        DateTimeOffset CreatedAt,
        string TeacherName,
        string InstrumentName,
        string? Practiced,
        string? Note,
        string? Homework,
        string? NextGoal,
        string? PieceTitle,
        int? PieceDifficulty,
        string? PieceComposer,
        RepertoireStatus? PieceStatus,
        DateOnly? PieceTargetDate,
        string? PieceResourceUrl,
        bool PieceResourceVisibleToGuardian,
        string? ParentComment,
        DateTimeOffset? ParentCommentApprovedAt);

    public record SkillAssessmentEntryResponse(
        Guid Id,
        Guid SkillDefinitionId,
        string SkillCode,
        string SkillLabel,
        Guid TeacherId,
        string TeacherName,
        Guid? LessonId,
        int Score,
        string? Note,
        DateTimeOffset AssessedAt);

    public record ProgressResponse(
        Guid StudentId,
        string StudentName,
        int EntryCount,
        DateTimeOffset? LastEntryAt,
        bool AiTransformationAvailable,
        IReadOnlyList<ProgressEntryResponse> Entries,
        IReadOnlyList<SkillAssessmentEntryResponse> SkillAssessments);

    public static void MapStudentProgress(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/students/{studentId:guid}/progress", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> ListAsync(
        Guid studentId,
        Guid? teacherId,
        Guid? instrumentId,
        int? difficulty,
        DateTimeOffset? lastWorkedFrom,
        ClaimsPrincipal principal,
        AbderaDbContext db)
    {
        var student = await db.Students.SingleOrDefaultAsync(s => s.Id == studentId)
            ?? throw new NotFoundException("Öğrenci bulunamadı.");

        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } scopedTeacherId)
        {
            var isAssigned = await db.Enrollments.AnyAsync(enrollment =>
                enrollment.StudentId == studentId && enrollment.TeacherId == scopedTeacherId);
            if (!isAssigned) throw new ForbiddenException("Bu öğrenci size atanmamış.");
        }

        var entryQuery =
            from note in db.LessonNotes
            join lesson in db.Lessons on note.LessonId equals lesson.Id
            join teacher in db.Teachers on note.TeacherId equals teacher.Id
            join instrument in db.Instruments on lesson.InstrumentId equals instrument.Id
            where lesson.StudentId == studentId
            select new { note, lesson, teacher, instrument };

        if (teacherScope is { } noteTeacherId)
            entryQuery = entryQuery.Where(item => item.note.TeacherId == noteTeacherId);

        if (teacherId is { } selectedTeacherId)
            entryQuery = entryQuery.Where(item => item.note.TeacherId == selectedTeacherId);
        if (instrumentId is { } selectedInstrumentId)
            entryQuery = entryQuery.Where(item => item.lesson.InstrumentId == selectedInstrumentId);
        if (difficulty is { } selectedDifficulty)
            entryQuery = entryQuery.Where(item => item.note.PieceDifficulty == selectedDifficulty);
        if (lastWorkedFrom is { } selectedLastWorkedFrom)
            entryQuery = entryQuery.Where(item => item.lesson.StartAt >= selectedLastWorkedFrom);

        var entries = await entryQuery
            .OrderByDescending(item => item.note.CreatedAt)
            .Select(item => new ProgressEntryResponse(
                item.note.Id,
                item.note.LessonId,
                item.note.TeacherId,
                item.lesson.InstrumentId,
                item.lesson.StartAt,
                item.note.CreatedAt,
                item.teacher.FirstName + " " + item.teacher.LastName,
                item.instrument.Name,
                item.note.Practiced,
                item.note.Note,
                item.note.Homework,
                item.note.NextGoal,
                item.note.PieceTitle,
                item.note.PieceDifficulty,
                item.note.PieceComposer,
                item.note.PieceStatus,
                item.note.PieceTargetDate,
                item.note.PieceResourceUrl,
                item.note.PieceResourceVisibleToGuardian,
                item.note.ParentComment,
                item.note.ParentCommentApprovedAt))
            .ToListAsync();

        var skillAssessments = await (
            from assessment in db.SkillAssessments
            join skill in db.SkillDefinitions on assessment.SkillDefinitionId equals skill.Id
            join teacher in db.Teachers on assessment.TeacherId equals teacher.Id
            where assessment.StudentId == studentId &&
                  (teacherScope == null || assessment.TeacherId == teacherScope.Value)
            orderby assessment.AssessedAt descending
            select new SkillAssessmentEntryResponse(
                assessment.Id,
                assessment.SkillDefinitionId,
                skill.Code,
                skill.Label,
                assessment.TeacherId,
                teacher.FirstName + " " + teacher.LastName,
                assessment.LessonId,
                assessment.Score,
                assessment.Note,
                assessment.AssessedAt)
        ).ToListAsync();

        var lastEntryAt = new DateTimeOffset?[]
        {
            entries.Count == 0 ? null : entries[0].CreatedAt,
            skillAssessments.Count == 0 ? null : skillAssessments[0].AssessedAt,
        }.Max();

        return Results.Ok(new ProgressResponse(
            student.Id,
            student.FirstName + " " + student.LastName,
            entries.Count,
            lastEntryAt,
            false,
            entries,
            skillAssessments));
    }

}
