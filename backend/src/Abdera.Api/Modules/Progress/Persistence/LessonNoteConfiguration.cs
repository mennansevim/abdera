using Abdera.Api.Modules.Progress.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Progress.Persistence;

public class LessonNoteConfiguration : IEntityTypeConfiguration<LessonNote>
{
    public void Configure(EntityTypeBuilder<LessonNote> builder)
    {
        builder.ToTable("lesson_notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id");
        builder.Property(n => n.LessonId).HasColumnName("lesson_id");
        builder.Property(n => n.TeacherId).HasColumnName("teacher_id");
        builder.Property(n => n.Practiced).HasColumnName("practiced");
        builder.Property(n => n.Note).HasColumnName("note");
        builder.Property(n => n.Homework).HasColumnName("homework");
        builder.Property(n => n.NextGoal).HasColumnName("next_goal");
        builder.Property(n => n.PieceTitle).HasColumnName("piece_title");
        builder.Property(n => n.PieceDifficulty).HasColumnName("piece_difficulty");
        builder.Property(n => n.PieceComposer).HasColumnName("piece_composer").HasMaxLength(200);
        builder.Property(n => n.PieceStatus).HasColumnName("piece_status").HasConversion<string>().HasMaxLength(30);
        builder.Property(n => n.PieceTargetDate).HasColumnName("piece_target_date");
        builder.Property(n => n.PieceResourceUrl).HasColumnName("piece_resource_url").HasMaxLength(2000);
        builder.Property(n => n.PieceResourceVisibleToGuardian).HasColumnName("piece_resource_visible_to_guardian");
        builder.Property(n => n.ParentComment).HasColumnName("parent_comment");
        builder.Property(n => n.ParentCommentApprovedAt).HasColumnName("parent_comment_approved_at");
        builder.Property(n => n.ParentCommentApprovedBy).HasColumnName("parent_comment_approved_by");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at");
        builder.Property(n => n.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(n => n.LessonId);
    }
}
