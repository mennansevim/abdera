using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class TeacherPayRateConfiguration : IEntityTypeConfiguration<TeacherPayRate>
{
    public void Configure(EntityTypeBuilder<TeacherPayRate> builder)
    {
        builder.ToTable("teacher_pay_rates");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.TeacherId).HasColumnName("teacher_id");
        builder.Property(r => r.AmountPerLesson).HasColumnName("amount_per_lesson").HasColumnType("numeric(12,2)");
        builder.Property(r => r.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("TRY");
        builder.Property(r => r.UpdatedBy).HasColumnName("updated_by");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        // Öğretmen başına tek geçerli ücret - sürümleme yok (TeacherPayRate sınıf notu).
        builder.HasIndex(r => r.TeacherId).IsUnique().HasDatabaseName("ix_teacher_pay_rates_teacher");
        builder.ToTable(t => t.HasCheckConstraint("CK_teacher_pay_rates_amount", "amount_per_lesson > 0"));
    }
}
