using Abdera.Api.Modules.Messaging.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Messaging.Persistence;

public class NotificationJobConfiguration : IEntityTypeConfiguration<NotificationJob>
{
    public void Configure(EntityTypeBuilder<NotificationJob> builder)
    {
        builder.ToTable("notification_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).HasColumnName("id");
        builder.Property(j => j.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(30);
        builder.Property(j => j.RecipientPhoneNumber).HasColumnName("recipient_phone_number").HasMaxLength(20);
        builder.Property(j => j.ReferenceType).HasColumnName("reference_type").HasMaxLength(30);
        builder.Property(j => j.ReferenceId).HasColumnName("reference_id");
        builder.Property(j => j.ScheduledAt).HasColumnName("scheduled_at");
        builder.Property(j => j.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.AttemptCount).HasColumnName("attempt_count").HasDefaultValue((short)0);
        builder.Property(j => j.LastError).HasColumnName("last_error");
        builder.Property(j => j.SentAt).HasColumnName("sent_at");
        builder.Property(j => j.CreatedAt).HasColumnName("created_at");
        builder.Property(j => j.UpdatedAt).HasColumnName("updated_at");

        // A5: idempotency anahtarı - aynı ders/aidat için ikinci job DB seviyesinde engellenir.
        builder.HasIndex(j => new { j.Type, j.ReferenceType, j.ReferenceId }).IsUnique();
        builder.HasIndex(j => new { j.Status, j.ScheduledAt });
    }
}
