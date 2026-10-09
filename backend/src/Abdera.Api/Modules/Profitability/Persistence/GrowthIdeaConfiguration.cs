using Abdera.Api.Modules.Profitability.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Profitability.Persistence;

public class GrowthIdeaConfiguration : IEntityTypeConfiguration<GrowthIdea>
{
    public void Configure(EntityTypeBuilder<GrowthIdea> builder)
    {
        builder.ToTable("growth_ideas");
        builder.HasKey(idea => idea.Id);
        builder.Property(idea => idea.Id).HasColumnName("id");
        builder.Property(idea => idea.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
        builder.Property(idea => idea.Title).HasColumnName("title").HasMaxLength(GrowthIdea.TitleMaxLength).IsRequired();
        builder.Property(idea => idea.Note).HasColumnName("note").HasMaxLength(GrowthIdea.NoteMaxLength);
        builder.Property(idea => idea.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(idea => idea.InstrumentId).HasColumnName("instrument_id");
        builder.Property(idea => idea.BranchName).HasColumnName("branch_name").HasMaxLength(GrowthIdea.BranchNameMaxLength);
        builder.Property(idea => idea.CourseKind).HasColumnName("course_kind").HasConversion<string>().HasMaxLength(20);
        builder.Property(idea => idea.Students).HasColumnName("students");
        builder.Property(idea => idea.TeacherRatePerLesson).HasColumnName("teacher_rate_per_lesson").HasColumnType("numeric(12,2)");
        builder.Property(idea => idea.DiscountPercent).HasColumnName("discount_percent").HasColumnType("numeric(5,2)");
        builder.Property(idea => idea.DiscountMonths).HasColumnName("discount_months");
        builder.Property(idea => idea.AlreadyComingPercent).HasColumnName("already_coming_percent");
        builder.Property(idea => idea.PriceChangePercent).HasColumnName("price_change_percent").HasColumnType("numeric(5,2)");
        builder.Property(idea => idea.LostStudents).HasColumnName("lost_students");
        // Tutarlar tahmini, yine de para kuralı: decimal(12,2) + ayrı currency kolonu.
        builder.Property(idea => idea.MonthlyAmount).HasColumnName("monthly_amount").HasColumnType("numeric(12,2)");
        builder.Property(idea => idea.OneTimeCost).HasColumnName("one_time_cost").HasColumnType("numeric(12,2)");
        builder.Property(idea => idea.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(idea => idea.CreatedBy).HasColumnName("created_by");
        builder.Property(idea => idea.CreatedAt).HasColumnName("created_at");
        builder.Property(idea => idea.UpdatedAt).HasColumnName("updated_at");
        // İki yönetici aynı fikri aynı anda düzenlerse biri 409 alır.
        builder.Property<uint>("Version").IsRowVersion();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_growth_ideas_students", "students IS NULL OR students BETWEEN 1 AND 200");
            table.HasCheckConstraint("ck_growth_ideas_one_time_cost", "one_time_cost IS NULL OR one_time_cost >= 0");
        });

        // Enstrüman silinmez (referans veri); yine de bağ açık olsun.
        builder.HasOne<Abdera.Api.Modules.People.Domain.Instrument>().WithMany()
            .HasForeignKey(idea => idea.InstrumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
