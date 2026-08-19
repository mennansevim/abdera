using Abdera.Api.Modules.Auth.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Auth.Persistence;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(200).IsRequired();
        builder.Property(a => a.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasColumnName("entity_id");
        builder.Property(a => a.BeforeJson).HasColumnName("before_json").HasColumnType("jsonb");
        builder.Property(a => a.AfterJson).HasColumnName("after_json").HasColumnType("jsonb");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        // Audit geçmişi hızlı sorgulanabilsin diye - dashboard/denetim ekranı bunu filtreler.
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}
