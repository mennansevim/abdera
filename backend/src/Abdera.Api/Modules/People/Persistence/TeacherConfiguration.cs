using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class TeacherConfiguration : IEntityTypeConfiguration<Teacher>
{
    public void Configure(EntityTypeBuilder<Teacher> builder)
    {
        builder.ToTable("teachers");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.UserId).HasColumnName("user_id");
        builder.Property(t => t.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
        builder.Property(t => t.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
        builder.Property(t => t.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(t => t.UserId).IsUnique().HasFilter("user_id IS NOT NULL");
    }
}
