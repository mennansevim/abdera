using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class InstrumentMaintenanceSettingConfiguration : IEntityTypeConfiguration<InstrumentMaintenanceSetting>
{
    public void Configure(EntityTypeBuilder<InstrumentMaintenanceSetting> builder)
    {
        builder.ToTable("instrument_maintenance_settings");
        builder.HasKey(setting => setting.Id);
        builder.Property(setting => setting.Id).HasColumnName("id");
        builder.Property(setting => setting.InstrumentId).HasColumnName("instrument_id");
        builder.Property(setting => setting.MaintenanceType).HasColumnName("maintenance_type").HasMaxLength(200).IsRequired();
        builder.Property(setting => setting.PeriodDays).HasColumnName("period_days");
        builder.Property(setting => setting.IsEnabled).HasColumnName("is_enabled");
        builder.Property(setting => setting.NotificationPreference).HasColumnName("notification_preference").HasConversion<string>();
        builder.Property(setting => setting.NextReminderAt).HasColumnName("next_reminder_at");
        builder.Property(setting => setting.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(setting => setting.InstrumentId).IsUnique();
        builder.HasOne<Instrument>().WithMany().HasForeignKey(setting => setting.InstrumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class InstrumentMaintenanceReminderConfiguration : IEntityTypeConfiguration<InstrumentMaintenanceReminder>
{
    public void Configure(EntityTypeBuilder<InstrumentMaintenanceReminder> builder)
    {
        builder.ToTable("instrument_maintenance_reminders");
        builder.HasKey(reminder => reminder.Id);
        builder.Property(reminder => reminder.Id).HasColumnName("id");
        builder.Property(reminder => reminder.SettingId).HasColumnName("setting_id");
        builder.Property(reminder => reminder.GuardianId).HasColumnName("guardian_id");
        builder.Property(reminder => reminder.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(reminder => new { reminder.SettingId, reminder.GuardianId, reminder.CreatedAt });
        builder.HasOne<InstrumentMaintenanceSetting>().WithMany().HasForeignKey(reminder => reminder.SettingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Guardian>().WithMany().HasForeignKey(reminder => reminder.GuardianId).OnDelete(DeleteBehavior.Restrict);
    }
}
