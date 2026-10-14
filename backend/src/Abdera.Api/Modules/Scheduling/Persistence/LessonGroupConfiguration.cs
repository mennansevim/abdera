using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Scheduling.Persistence;

public class LessonGroupConfiguration : IEntityTypeConfiguration<LessonGroup>
{
    public void Configure(EntityTypeBuilder<LessonGroup> builder)
    {
        builder.ToTable("lesson_groups");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id");
        builder.Property(g => g.Name).HasColumnName("name").HasMaxLength(LessonGroup.NameMaxLength).IsRequired();
        builder.Property(g => g.TeacherId).HasColumnName("teacher_id");
        builder.Property(g => g.InstrumentId).HasColumnName("instrument_id");
        builder.Property(g => g.DayOfWeek).HasColumnName("day_of_week").HasConversion<int>();
        builder.Property(g => g.StartTime).HasColumnName("start_time");
        builder.Property(g => g.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(g => g.Capacity).HasColumnName("capacity");
        builder.Property(g => g.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(g => g.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(g => g.EndedOn).HasColumnName("ended_on");
        builder.Property(g => g.CreatedAt).HasColumnName("created_at");
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at");
        // İki yönetici son boş yere aynı anda öğrenci ekleyemesin (LessonGroup.EnsureCanAdmit).
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(g => new { g.TeacherId, g.Status });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_lesson_groups_capacity", "capacity BETWEEN 2 AND 12");
            table.HasCheckConstraint("ck_lesson_groups_duration", "duration_minutes BETWEEN 15 AND 180");
        });

        builder.HasOne<Abdera.Api.Modules.People.Domain.Teacher>().WithMany()
            .HasForeignKey(g => g.TeacherId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Abdera.Api.Modules.People.Domain.Instrument>().WithMany()
            .HasForeignKey(g => g.InstrumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
