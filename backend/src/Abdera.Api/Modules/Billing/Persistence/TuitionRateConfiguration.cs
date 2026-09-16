using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class TuitionRateConfiguration : IEntityTypeConfiguration<TuitionRate>
{
    public void Configure(EntityTypeBuilder<TuitionRate> builder)
    {
        builder.ToTable("tuition_rates");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CourseKind).HasColumnName("course_kind").HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.LessonsPerMonth).HasColumnName("lessons_per_month");
        builder.Property(r => r.MonthlyAmount).HasColumnName("monthly_amount").HasColumnType("numeric(12,2)");
        builder.Property(r => r.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("TRY");
        builder.Property(r => r.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(r => r.EffectiveUntil).HasColumnName("effective_until");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.CreatedBy).HasColumnName("created_by");

        builder.HasIndex(r => new { r.CourseKind, r.EffectiveFrom });
        builder.ToTable(t => t.HasCheckConstraint("CK_tuition_rates_amount", "monthly_amount >= 0"));
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_tuition_rates_effective_range", "effective_until IS NULL OR effective_until >= effective_from"));

        // Aynı ders türü için açık uçlu (henüz kapatılmamış) yalnızca BİR tarife olabilir.
        // Çakışan tarih aralıklarının tamamı tek bir kısıtla ifade edilemez (CLAUDE.md
        // price_list_items notundaki gerekçenin aynısı) ama en sık yapılacak hatayı -
        // eskisini kapatmadan yenisini açmak - veritabanı seviyesinde engeller.
        builder.HasIndex(r => r.CourseKind)
            .IsUnique()
            .HasFilter("effective_until IS NULL")
            .HasDatabaseName("ix_tuition_rates_one_open_per_kind");
    }
}
