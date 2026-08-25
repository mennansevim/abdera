using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

// docs/05-state-models.md - Lesson durum makinesi.
public class LessonStateTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    private static Lesson CreateNormalLesson() => Lesson.CreateFromSeries(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Now.AddDays(1), Now.AddDays(1).AddMinutes(45), Now);

    [Fact]
    public void Cancel_transitions_normal_lesson_to_cancelled()
    {
        var lesson = CreateNormalLesson();
        lesson.Cancel(Now);
        Assert.Equal(LessonStatus.Cancelled, lesson.Status);
    }

    [Fact]
    public void Cancel_throws_when_already_cancelled()
    {
        var lesson = CreateNormalLesson();
        lesson.Cancel(Now);
        Assert.Throws<ConflictException>(() => lesson.Cancel(Now));
    }

    [Fact]
    public void Cancel_throws_when_already_completed()
    {
        var lesson = CreateNormalLesson();
        lesson.Complete(Now);
        Assert.Throws<ConflictException>(() => lesson.Cancel(Now));
    }

    [Fact]
    public void Complete_throws_when_lesson_is_cancelled()
    {
        var lesson = CreateNormalLesson();
        lesson.Cancel(Now);
        Assert.Throws<ConflictException>(() => lesson.Complete(Now));
    }

    [Fact]
    public void CreateRescheduled_marks_original_as_rescheduled_and_returns_normal_copy()
    {
        var original = CreateNormalLesson();
        var newStart = Now.AddDays(2);
        var newEnd = newStart.AddMinutes(45);

        var rescheduled = Lesson.CreateRescheduled(original, newStart, newEnd, Now);

        Assert.Equal(LessonStatus.Rescheduled, original.Status);
        Assert.Equal(LessonStatus.Normal, rescheduled.Status);
        Assert.Equal(original.Id, rescheduled.OriginalLessonId);
        Assert.Equal(original.StudentId, rescheduled.StudentId);
        Assert.Equal(original.TeacherId, rescheduled.TeacherId);
        Assert.Equal(newStart, rescheduled.StartAt);
    }

    [Fact]
    public void CreateRescheduled_throws_when_original_is_not_normal()
    {
        var original = CreateNormalLesson();
        original.Cancel(Now);

        Assert.Throws<ConflictException>(() => Lesson.CreateRescheduled(original, Now.AddDays(2), Now.AddDays(2).AddMinutes(45), Now));
    }

    [Fact]
    public void CreateMakeup_has_no_lesson_series_and_makeup_status()
    {
        var makeup = Lesson.CreateMakeup(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddMinutes(45), Now);

        Assert.Null(makeup.LessonSeriesId);
        Assert.Equal(LessonStatus.Makeup, makeup.Status);
    }

    [Fact]
    public void CreateEditedCopy_preserves_history_and_detaches_series_when_participants_change()
    {
        var original = CreateNormalLesson();
        var studentId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var start = Now.AddDays(3);

        var edited = Lesson.CreateEditedCopy(original, studentId, teacherId, start, start.AddMinutes(60), Now);

        Assert.Equal(LessonStatus.Rescheduled, original.Status);
        Assert.Equal(LessonStatus.Normal, edited.Status);
        Assert.Equal(original.Id, edited.OriginalLessonId);
        Assert.Null(edited.LessonSeriesId);
        Assert.Equal(studentId, edited.StudentId);
        Assert.Equal(teacherId, edited.TeacherId);
        Assert.Equal(start.AddMinutes(60), edited.EndAt);
    }

    [Fact]
    public void CreateEditedCopy_keeps_series_on_current_copy_and_detaches_historical_row_when_only_duration_changes()
    {
        var seriesId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var start = Now.AddDays(3);
        var original = Lesson.CreateFromSeries(seriesId, studentId, teacherId, Guid.NewGuid(), start, start.AddMinutes(45), Now);

        var edited = Lesson.CreateEditedCopy(original, studentId, teacherId, start, start.AddMinutes(60), Now.AddMinutes(1));

        Assert.Null(original.LessonSeriesId);
        Assert.Equal(seriesId, edited.LessonSeriesId);
        Assert.Equal(original.Id, edited.OriginalLessonId);
        Assert.Equal(60, (edited.EndAt - edited.StartAt).TotalMinutes);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(181)]
    public void CreateEditedCopy_rejects_unreasonable_duration(int durationMinutes)
    {
        var original = CreateNormalLesson();
        var start = Now.AddDays(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => Lesson.CreateEditedCopy(
            original,
            original.StudentId,
            original.TeacherId,
            start,
            start.AddMinutes(durationMinutes),
            Now));
    }
}
