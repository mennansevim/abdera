using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class TeacherInstrumentConfiguration : IEntityTypeConfiguration<TeacherInstrument>
{
    public void Configure(EntityTypeBuilder<TeacherInstrument> builder)
    {
        builder.ToTable("teacher_instruments");
        builder.HasKey(ti => new { ti.TeacherId, ti.InstrumentId });
        builder.Property(ti => ti.TeacherId).HasColumnName("teacher_id");
        builder.Property(ti => ti.InstrumentId).HasColumnName("instrument_id");

        builder.HasOne<Teacher>().WithMany().HasForeignKey(ti => ti.TeacherId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Instrument>().WithMany().HasForeignKey(ti => ti.InstrumentId).OnDelete(DeleteBehavior.Cascade);
    }
}
