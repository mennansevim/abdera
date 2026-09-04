using Abdera.Api.Modules.Messaging.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Messaging.Persistence;

public class StaffNotificationConfiguration : IEntityTypeConfiguration<StaffNotification>
{
    public void Configure(EntityTypeBuilder<StaffNotification> builder)
    {
        builder.ToTable("staff_notifications");
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).HasColumnName("id");
        builder.Property(notification => notification.UserId).HasColumnName("user_id");
        builder.Property(notification => notification.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(30);
        builder.Property(notification => notification.Title).HasColumnName("title").HasMaxLength(120);
        builder.Property(notification => notification.Body).HasColumnName("body").HasMaxLength(500);
        builder.Property(notification => notification.ReferenceType).HasColumnName("reference_type").HasMaxLength(30);
        builder.Property(notification => notification.ReferenceId).HasColumnName("reference_id");
        builder.Property(notification => notification.ReadAt).HasColumnName("read_at");
        builder.Property(notification => notification.CreatedAt).HasColumnName("created_at");
        builder.Property(notification => notification.UpdatedAt).HasColumnName("updated_at");

        // Idempotency: aynı olay (örn. tek bir ders taşıma) aynı kullanıcıya iki kez düşmesin.
        // notification_jobs'taki A5 anahtarının ekran içi karşılığı - kullanıcı da anahtarın
        // parçası, çünkü aynı ders iki farklı öğretmeni ilgilendirebilir (ders devri).
        builder.HasIndex(notification => new
        {
            notification.UserId,
            notification.Type,
            notification.ReferenceType,
            notification.ReferenceId,
        }).IsUnique();

        // Zil rozeti "okunmamışları en yeniden eskiye" sorgular - listenin tek sorgusu bu.
        builder.HasIndex(notification => new { notification.UserId, notification.CreatedAt });
    }
}
