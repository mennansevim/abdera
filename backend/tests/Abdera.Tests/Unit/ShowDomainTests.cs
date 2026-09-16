using Abdera.Api.Modules.Show.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

public class ShowDomainTests
{
    private static readonly DateTimeOffset Now = new(2027, 6, 14, 15, 0, 0, TimeSpan.Zero);

    private static ShowEvent CreateShow() =>
        ShowEvent.Create("2027 Yıl Sonu Gösterisi", "Kültür Merkezi", Now.AddDays(7), Now);

    [Fact]
    public void Create_requires_a_title()
    {
        Assert.Throws<ArgumentException>(() => ShowEvent.Create("  ", null, Now, Now));
    }

    [Fact]
    public void Moving_the_stage_pointer_requires_a_started_show()
    {
        var show = CreateShow();

        // Taslak hâldeyken sıra ilerletmek sessizce çalışsaydı, gösteri başlamadan
        // sahne ekranı "şu an çalıyor" gösterirdi.
        Assert.Throws<ConflictException>(() => show.MoveTo(Guid.NewGuid(), Now));

        show.Start(Now);
        var itemId = Guid.NewGuid();
        show.MoveTo(itemId, Now);
        Assert.Equal(itemId, show.CurrentItemId);
    }

    [Fact]
    public void Start_is_idempotent_and_keeps_the_original_start_time()
    {
        var show = CreateShow();
        show.Start(Now);
        var startedAt = show.StartedAt;

        show.Start(Now.AddMinutes(10));

        Assert.Equal(startedAt, show.StartedAt);
        Assert.Equal(ShowEventStatus.Live, show.Status);
    }

    [Fact]
    public void Finish_clears_the_pointer_and_blocks_restart()
    {
        var show = CreateShow();
        show.Start(Now);
        show.MoveTo(Guid.NewGuid(), Now);

        show.Finish(Now.AddHours(2));

        Assert.Equal(ShowEventStatus.Completed, show.Status);
        Assert.Null(show.CurrentItemId);
        Assert.Throws<ConflictException>(() => show.Start(Now.AddHours(3)));
    }

    [Fact]
    public void Reopen_recovers_from_an_accidental_finish()
    {
        var show = CreateShow();
        show.Start(Now);
        show.Finish(Now.AddHours(1));

        show.ReopenAsDraft(Now.AddHours(1));

        Assert.Equal(ShowEventStatus.Draft, show.Status);
        Assert.Null(show.StartedAt);
        show.Start(Now.AddHours(1));
        Assert.Equal(ShowEventStatus.Live, show.Status);
    }

    [Fact]
    public void Performance_item_requires_a_student_and_a_piece()
    {
        var showId = Guid.NewGuid();

        Assert.Throws<ValidationFailedException>(() => ShowItem.Create(
            showId, 0, ShowItemKind.Performance, "1. Bölüm",
            studentId: null, null, null, "Für Elise", "Beethoven", 4, null, Now));

        Assert.Throws<ValidationFailedException>(() => ShowItem.Create(
            showId, 0, ShowItemKind.Performance, "1. Bölüm",
            Guid.NewGuid(), null, null, pieceTitle: "  ", composer: "Beethoven", 4, null, Now));
    }

    [Fact]
    public void Intermission_drops_performer_fields_so_the_stage_screen_cannot_show_a_stale_name()
    {
        var item = ShowItem.Create(
            Guid.NewGuid(), 3, ShowItemKind.Performance, "1. Bölüm",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Für Elise", "Beethoven", 4, null, Now);

        item.Apply(ShowItemKind.Intermission, "Ara", item.StudentId, item.InstrumentId, item.TeacherId,
            null, null, 15, "15 dakika ara", Now);

        Assert.Null(item.StudentId);
        Assert.Null(item.InstrumentId);
        Assert.Null(item.TeacherId);
        Assert.Null(item.Composer);
        Assert.Equal(15, item.DurationMinutes);
    }

    [Fact]
    public void Duration_outside_the_allowed_range_is_rejected()
    {
        Assert.Throws<ValidationFailedException>(() => ShowItem.Create(
            Guid.NewGuid(), 0, ShowItemKind.Performance, null,
            Guid.NewGuid(), null, null, "Eser", null, durationMinutes: 0, null, Now));

        Assert.Throws<ValidationFailedException>(() => ShowItem.Create(
            Guid.NewGuid(), 0, ShowItemKind.Performance, null,
            Guid.NewGuid(), null, null, "Eser", null, durationMinutes: 121, null, Now));
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("")]
    public void Photo_rejects_unsupported_content_types(string contentType)
    {
        Assert.Throws<ValidationFailedException>(() =>
            StudentPhoto.Create(Guid.NewGuid(), contentType, [1, 2, 3], Now));
    }

    [Fact]
    public void Photo_rejects_empty_and_oversized_files()
    {
        Assert.Throws<ValidationFailedException>(() =>
            StudentPhoto.Create(Guid.NewGuid(), "image/png", [], Now));
        Assert.Throws<ValidationFailedException>(() =>
            StudentPhoto.Create(Guid.NewGuid(), "image/png", new byte[StudentPhoto.MaxBytes + 1], Now));
    }

    [Fact]
    public void Replacing_a_photo_changes_the_version_so_caches_are_invalidated()
    {
        var photo = StudentPhoto.Create(Guid.NewGuid(), "image/png", [1, 2, 3], Now);
        var firstVersion = photo.Version;

        photo.Replace("image/jpeg", [4, 5, 6], Now.AddMinutes(1));

        Assert.NotEqual(firstVersion, photo.Version);
        Assert.Equal("image/jpeg", photo.ContentType);
    }
}
