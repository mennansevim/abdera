using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

// Ses Defteri (issue #52): format tanıma, boyut/süre sınırları ve budama kuralı saf birim
// testleriyle korunur. Budama kuralı yolculuk sayfasının dayandığı kayıtları (eser başına ilk
// ve son, yıldızlılar) asla silmemeli.
public class AudioClipDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Detects_containers_from_magic_bytes_not_from_the_client_header()
    {
        Assert.Equal(AudioFormat.WebM, AudioFormat.Detect([0x1A, 0x45, 0xDF, 0xA3, 0x01]));
        Assert.Equal(AudioFormat.Ogg, AudioFormat.Detect("OggS\0"u8));
        Assert.Equal(AudioFormat.Mp4, AudioFormat.Detect("\0\0\0\u0018ftypM4A "u8));
        Assert.Null(AudioFormat.Detect("%PDF-1.7"u8));
        Assert.Null(AudioFormat.Detect([]));
    }

    [Theory]
    [InlineData(500, 30, "file")]
    [InlineData(AudioClip.MaxBytes + 1, 30, "file")]
    [InlineData(200_000, 0, "durationSeconds")]
    [InlineData(200_000, 61, "durationSeconds")]
    public void Rejects_clips_outside_size_and_duration_limits(int sizeBytes, int durationSeconds, string field)
    {
        var error = Assert.Throws<ValidationFailedException>(() => Create(sizeBytes: sizeBytes, durationSeconds: durationSeconds));
        Assert.Contains(field, error.Errors.Keys);
    }

    [Fact]
    public void File_name_is_server_generated_from_the_id_and_detected_format()
    {
        var clip = Create(pieceTitle: "  Für Elise  ");
        Assert.Equal($"{clip.Id:N}.webm", clip.FileName);
        Assert.Equal("audio/webm", clip.ContentType);
        Assert.Equal("Für Elise", clip.PieceTitle);
        Assert.False(clip.SharedWithGuardian);
    }

    [Fact]
    public void Sharing_records_who_and_when_and_can_be_withdrawn()
    {
        var clip = Create();
        var teacher = Guid.NewGuid();
        clip.SetSharedWithGuardian(true, teacher, Now);
        Assert.True(clip.SharedWithGuardian);
        Assert.Equal(teacher, clip.SharedByUserId);

        clip.SetSharedWithGuardian(false, teacher, Now);
        Assert.False(clip.SharedWithGuardian);
        Assert.Null(clip.SharedByUserId);
    }

    [Fact]
    public void Retention_keeps_first_and_last_take_of_each_piece_and_starred_clips()
    {
        var old = Now.AddMonths(-18);
        var first = Create("Für Elise", old);
        var middle = Create("für  elise", old.AddDays(7));
        var starred = Create("Für Elise", old.AddDays(14));
        starred.SetStarred(true, Now);
        var middle2 = Create("Für Elise", old.AddDays(21));
        var last = Create("Für Elise", Now.AddMonths(-1));
        var recentMiddle = Create("Für Elise", Now.AddMonths(-2));

        var prunable = AudioClipRetention.SelectPrunable(
            [first, middle, starred, middle2, last, recentMiddle], Now, retentionMonths: 12);

        Assert.Equal(
            new HashSet<Guid> { middle.Id, middle2.Id },
            prunable.Select(clip => clip.Id).ToHashSet());
    }

    [Fact]
    public void Retention_never_prunes_a_piece_with_only_two_takes()
    {
        var clips = new[] { Create("Minuet", Now.AddYears(-3)), Create("Minuet", Now.AddYears(-2)) };
        Assert.Empty(AudioClipRetention.SelectPrunable(clips, Now, retentionMonths: 12));
    }

    private static AudioClip Create(string? pieceTitle, DateTimeOffset createdAt) =>
        AudioClip.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), pieceTitle, AudioFormat.WebM, 200_000, 30, Guid.NewGuid(), createdAt);

    private static AudioClip Create(int sizeBytes = 200_000, int durationSeconds = 30, string? pieceTitle = null) =>
        AudioClip.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), pieceTitle, AudioFormat.WebM, sizeBytes, durationSeconds, Guid.NewGuid(), Now);
}
