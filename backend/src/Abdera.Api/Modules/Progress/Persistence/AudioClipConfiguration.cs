using Abdera.Api.Modules.Progress.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Progress.Persistence;

public class AudioClipConfiguration : IEntityTypeConfiguration<AudioClip>
{
    public void Configure(EntityTypeBuilder<AudioClip> builder)
    {
        builder.ToTable("audio_clips", table =>
        {
            table.HasCheckConstraint("ck_audio_clips_size", $"size_bytes > 0 AND size_bytes <= {AudioClip.MaxBytes}");
            table.HasCheckConstraint("ck_audio_clips_duration", $"duration_seconds BETWEEN 1 AND {AudioClip.MaxDurationSeconds}");
        });
        builder.HasKey(clip => clip.Id);
        builder.Property(clip => clip.Id).HasColumnName("id");
        builder.Property(clip => clip.StudentId).HasColumnName("student_id");
        builder.Property(clip => clip.TeacherId).HasColumnName("teacher_id");
        builder.Property(clip => clip.LessonId).HasColumnName("lesson_id");
        builder.Property(clip => clip.PieceTitle).HasColumnName("piece_title").HasMaxLength(AudioClip.MaxPieceTitleLength);
        builder.Property(clip => clip.FileName).HasColumnName("file_name").HasMaxLength(64).IsRequired();
        builder.Property(clip => clip.ContentType).HasColumnName("content_type").HasMaxLength(32).IsRequired();
        builder.Property(clip => clip.SizeBytes).HasColumnName("size_bytes");
        builder.Property(clip => clip.DurationSeconds).HasColumnName("duration_seconds");
        builder.Property(clip => clip.Starred).HasColumnName("starred").HasDefaultValue(false);
        builder.Property(clip => clip.SharedWithGuardianAt).HasColumnName("shared_with_guardian_at");
        builder.Property(clip => clip.SharedByUserId).HasColumnName("shared_by_user_id");
        builder.Property(clip => clip.BackedUpAt).HasColumnName("backed_up_at");
        builder.Property(clip => clip.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(clip => clip.CreatedAt).HasColumnName("created_at");
        builder.Property(clip => clip.UpdatedAt).HasColumnName("updated_at");
        builder.Ignore(clip => clip.SharedWithGuardian);

        builder.HasIndex(clip => clip.FileName).IsUnique();
        builder.HasIndex(clip => new { clip.StudentId, clip.CreatedAt });
        builder.HasIndex(clip => clip.LessonId);
        // Gece bakımının "henüz yedeklenmemiş" taraması tüm tabloyu dolaşmasın.
        builder.HasIndex(clip => clip.CreatedAt).HasFilter("backed_up_at IS NULL")
            .HasDatabaseName("ix_audio_clips_not_backed_up");
        // PracticeJournalEntry ile aynı: öğrenci silme PersonEraser'dan geçer, cascade yok.
        builder.HasOne<Abdera.Api.Modules.People.Domain.Student>().WithMany()
            .HasForeignKey(clip => clip.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}
