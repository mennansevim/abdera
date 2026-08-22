using Abdera.Api.Modules.Messaging.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Messaging.Persistence;

public class NotificationAutomationSettingsConfiguration : IEntityTypeConfiguration<NotificationAutomationSettings>
{
    public void Configure(EntityTypeBuilder<NotificationAutomationSettings> builder)
    {
        builder.ToTable("notification_automation_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.LessonReminderMinutesBefore).HasColumnName("lesson_reminder_minutes_before");
        builder.Property(s => s.IsEnabled).HasColumnName("is_enabled");
        builder.Property(s => s.AllowAttendingLateResponse).HasColumnName("allow_attending_late_response");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
    }
}
