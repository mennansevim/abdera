using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Scheduling.Domain;

namespace Abdera.Tests.Unit;

public class ProgressDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void LessonNote_Create_trims_teacher_content_and_preserves_references()
    {
        var lessonId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();

        var note = LessonNote.Create(
            lessonId,
            teacherId,
            "  Do majör gamı  ",
            "  Ritim daha dengeli.  ",
            "  Metronomla 15 dakika  ",
            "  80 BPM'e çıkmak  ",
            "  Bach · Minuet in G  ",
            4,
            Now);

        Assert.NotEqual(Guid.Empty, note.Id);
        Assert.Equal(lessonId, note.LessonId);
        Assert.Equal(teacherId, note.TeacherId);
        Assert.Equal("Do majör gamı", note.Practiced);
        Assert.Equal("Ritim daha dengeli.", note.Note);
        Assert.Equal("Metronomla 15 dakika", note.Homework);
        Assert.Equal("80 BPM'e çıkmak", note.NextGoal);
        Assert.Equal("Bach · Minuet in G", note.PieceTitle);
        Assert.Equal(4, note.PieceDifficulty);
        Assert.Equal(Now, note.CreatedAt);
    }

    [Fact]
    public void LessonNote_Create_normalizes_empty_optional_fields_to_null()
    {
        var note = LessonNote.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, "", "  ", null, "\t", null, Now);

        Assert.Null(note.Practiced);
        Assert.Null(note.Note);
        Assert.Null(note.Homework);
        Assert.Null(note.NextGoal);
        Assert.Null(note.PieceTitle);
        Assert.Null(note.PieceDifficulty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(100)]
    public void LessonNote_Create_rejects_piece_difficulty_outside_one_to_five(int difficulty)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LessonNote.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, "Eser", difficulty, Now));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LessonNote_Create_accepts_every_valid_piece_difficulty(int difficulty)
    {
        var note = LessonNote.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, "Eser", difficulty, Now);

        Assert.Equal(difficulty, note.PieceDifficulty);
    }

    [Fact]
    public void LessonNote_parent_comment_requires_explicit_teacher_approval_and_can_be_revoked()
    {
        var teacherId = Guid.NewGuid();
        var note = LessonNote.Create(Guid.NewGuid(), teacherId, null, "Ham ve yalnız öğretmene ait not", null, null, null, null, Now);

        note.SetParentCommentDraft("Yapıcı veli yorumu", Now.AddMinutes(1));
        Assert.Null(note.ParentCommentApprovedAt);

        note.ApproveParentComment(teacherId, Now.AddMinutes(2));
        Assert.Equal(Now.AddMinutes(2), note.ParentCommentApprovedAt);
        Assert.Equal(teacherId, note.ParentCommentApprovedBy);

        note.RevokeParentComment(teacherId, Now.AddMinutes(3));
        Assert.Null(note.ParentCommentApprovedAt);
        Assert.Null(note.ParentCommentApprovedBy);
        Assert.Equal("Ham ve yalnız öğretmene ait not", note.Note);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not-a-url")]
    [InlineData("file:///tmp/score.pdf")]
    public void LessonNote_rejects_unsafe_piece_resource_links(string resourceUrl)
    {
        Assert.Throws<ArgumentException>(() => LessonNote.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, "Eser", 2, Now,
            pieceResourceUrl: resourceUrl));
    }

    [Theory]
    [InlineData("2026-08-24", "2026-08-24")] // Pazartesi
    [InlineData("2026-08-25", "2026-08-24")]
    [InlineData("2026-08-26", "2026-08-24")]
    [InlineData("2026-08-27", "2026-08-24")]
    [InlineData("2026-08-28", "2026-08-24")]
    [InlineData("2026-08-29", "2026-08-24")]
    [InlineData("2026-08-30", "2026-08-24")] // Pazar
    public void StudentWeeklyLessonPolicy_StartOfWeek_uses_Monday_for_every_day(
        string input,
        string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), StudentWeeklyLessonPolicy.StartOfWeek(DateOnly.Parse(input)));
    }

    [Theory]
    [InlineData("2026-01-01", "2025-12-29")]
    [InlineData("2028-03-01", "2028-02-28")]
    [InlineData("2026-12-31", "2026-12-28")]
    public void StudentWeeklyLessonPolicy_StartOfWeek_handles_month_year_and_leap_boundaries(
        string input,
        string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), StudentWeeklyLessonPolicy.StartOfWeek(DateOnly.Parse(input)));
    }

    [Fact]
    public void SkillDefinition_Create_normalizes_code_and_label()
    {
        var instrumentId = Guid.NewGuid();

        var skill = SkillDefinition.Create("  hand_coordination ", "  El Koordinasyonu  ", instrumentId);

        Assert.NotEqual(Guid.Empty, skill.Id);
        Assert.Equal("HAND_COORDINATION", skill.Code);
        Assert.Equal("El Koordinasyonu", skill.Label);
        Assert.Equal(instrumentId, skill.InstrumentId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void SkillAssessment_Create_rejects_score_outside_one_to_five(int score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SkillAssessment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, score, null, Now));
    }

    [Fact]
    public void SkillAssessment_Create_trims_note_and_preserves_lesson_attribution()
    {
        var lessonId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();

        var assessment = SkillAssessment.Create(
            Guid.NewGuid(), Guid.NewGuid(), teacherId, lessonId, 5, "  Belirgin ilerleme  ", Now);

        Assert.Equal(teacherId, assessment.TeacherId);
        Assert.Equal(lessonId, assessment.LessonId);
        Assert.Equal(5, assessment.Score);
        Assert.Equal("Belirgin ilerleme", assessment.Note);
        Assert.Equal(Now, assessment.AssessedAt);
    }

    [Fact]
    public void PracticeAssignment_Create_trims_description_and_starts_incomplete()
    {
        var lessonId = Guid.NewGuid();
        var dueDate = new DateOnly(2026, 9, 1);

        var assignment = PracticeAssignment.Create(lessonId, "  Her gün 15 dakika metronom  ", dueDate, Now);

        Assert.Equal(lessonId, assignment.LessonId);
        Assert.Equal("Her gün 15 dakika metronom", assignment.Description);
        Assert.Equal(dueDate, assignment.DueDate);
        Assert.False(assignment.Completed);
        Assert.Equal(Now, assignment.CreatedAt);
        Assert.Equal(Now, assignment.UpdatedAt);
    }

    [Fact]
    public void PracticeAssignment_MarkCompleted_is_one_way()
    {
        var assignment = PracticeAssignment.Create(Guid.NewGuid(), "Gam", null, Now);
        var completedAt = Now.AddDays(1);

        assignment.MarkCompleted(completedAt);

        Assert.True(assignment.Completed);
        Assert.Equal(completedAt, assignment.UpdatedAt);
        Assert.Throws<Abdera.Api.Shared.ConflictException>(() => assignment.MarkCompleted(completedAt.AddMinutes(1)));
    }
}
