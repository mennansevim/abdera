using Abdera.Api.Modules.Progress.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Progress.Persistence;

public class SkillDefinitionConfiguration : IEntityTypeConfiguration<SkillDefinition>
{
    public void Configure(EntityTypeBuilder<SkillDefinition> builder)
    {
        builder.ToTable("skill_definitions");
        builder.HasKey(skill => skill.Id);
        builder.Property(skill => skill.Id).HasColumnName("id");
        builder.Property(skill => skill.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(skill => skill.Label).HasColumnName("label").HasMaxLength(100).IsRequired();
        builder.Property(skill => skill.InstrumentId).HasColumnName("instrument_id");
        builder.HasIndex(skill => skill.Code).IsUnique();
        builder.HasOne<Abdera.Api.Modules.People.Domain.Instrument>()
            .WithMany()
            .HasForeignKey(skill => skill.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
