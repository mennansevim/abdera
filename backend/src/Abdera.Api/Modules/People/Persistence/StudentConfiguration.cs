using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("students");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
        builder.Property(s => s.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
        builder.Property(s => s.BirthDate).HasColumnName("birth_date");
        builder.Property(s => s.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(s => new { s.LastName, s.FirstName });
    }
}
