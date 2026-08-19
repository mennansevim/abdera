using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.ToTable("instruments");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(i => i.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        builder.HasIndex(i => i.Name).IsUnique();
        builder.HasIndex(i => i.Code).IsUnique();
    }
}
