using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Scheduling.Persistence;

public class SchoolCalendarDayConfiguration : IEntityTypeConfiguration<SchoolCalendarDay>
{
    public void Configure(EntityTypeBuilder<SchoolCalendarDay> builder)
    {
        builder.ToTable("school_calendar_days");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.Date).HasColumnName("date");
        builder.Property(d => d.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Label).HasColumnName("label").HasMaxLength(200).IsRequired();

        builder.HasIndex(d => d.Date).IsUnique();
    }
}
