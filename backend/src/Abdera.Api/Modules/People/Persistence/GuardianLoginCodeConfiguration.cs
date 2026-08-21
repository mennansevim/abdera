using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class GuardianLoginCodeConfiguration : IEntityTypeConfiguration<GuardianLoginCode>
{
    public void Configure(EntityTypeBuilder<GuardianLoginCode> builder)
    {
        builder.ToTable("guardian_login_codes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.GuardianId).HasColumnName("guardian_id");
        builder.Property(c => c.CodeHash).HasColumnName("code_hash").IsRequired();
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(c => c.Attempts).HasColumnName("attempts").HasDefaultValue(0);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        // En güncel geçerli kodu bulmak için (GuardianId, ExpiresAt DESC) taraması burada olur.
        builder.HasIndex(c => new { c.GuardianId, c.ExpiresAt });
    }
}
