using Abdera.Api.Modules.Show.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Show.Persistence;

public class ShowItemConfiguration : IEntityTypeConfiguration<ShowItem>
{
    public void Configure(EntityTypeBuilder<ShowItem> builder)
    {
        builder.ToTable("show_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.ShowEventId).HasColumnName("show_event_id");
        builder.Property(i => i.Position).HasColumnName("position");
        builder.Property(i => i.GroupName).HasColumnName("group_name").HasMaxLength(200);
        builder.Property(i => i.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.StudentId).HasColumnName("student_id");
        builder.Property(i => i.InstrumentId).HasColumnName("instrument_id");
        builder.Property(i => i.TeacherId).HasColumnName("teacher_id");
        builder.Property(i => i.PieceTitle).HasColumnName("piece_title").HasMaxLength(200);
        builder.Property(i => i.Composer).HasColumnName("composer").HasMaxLength(200);
        builder.Property(i => i.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(i => i.Note).HasColumnName("note").HasMaxLength(200);
        builder.Property(i => i.CreatedAt).HasColumnName("created_at");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");

        // Gösteri silinince programı da gider - aidat/yoklama gibi mali veya geçmiş
        // kaydı değil, yalnızca bir organizasyon planı.
        builder.HasOne<ShowEvent>().WithMany()
            .HasForeignKey(i => i.ShowEventId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => new { i.ShowEventId, i.Position });
        builder.HasIndex(i => i.StudentId);

        builder.ToTable(t => t.HasCheckConstraint("CK_show_items_position", "position >= 0"));
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_show_items_duration", "duration_minutes IS NULL OR duration_minutes BETWEEN 1 AND 120"));
    }
}
