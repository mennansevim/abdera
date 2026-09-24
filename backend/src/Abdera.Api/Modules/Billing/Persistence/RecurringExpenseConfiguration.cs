using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class RecurringExpenseConfiguration : IEntityTypeConfiguration<RecurringExpense>
{
    public void Configure(EntityTypeBuilder<RecurringExpense> builder)
    {
        builder.ToTable("recurring_expenses");
        builder.HasKey(e => e.Id);
        // Id'yi domain üretir. ValueGeneratedNever olmazsa EF, Amounts koleksiyonuna eklenen ve
        // anahtarı dolu gelen yeni sürümü "mevcut satır" sanıp INSERT yerine UPDATE atar.
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(e => e.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(120);
        builder.Property(e => e.Note).HasColumnName("note");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        // Her tutar değişikliği kalemin UpdatedAt'ini de yazar; iki yönetici aynı kalemi aynı
        // anda değiştirirse xmin biri reddeder (ReceivableConfiguration'daki gerekçe).
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(e => e.Amounts).WithOne().HasForeignKey(a => a.RecurringExpenseId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(e => e.Amounts).HasField("_amounts").UsePropertyAccessMode(PropertyAccessMode.Field);
        // Hesaplanan özellikler - aksi halde EF OpenAmount'u ikinci bir ilişki sanıp gölge FK açar.
        builder.Ignore(e => e.OpenAmount);
        builder.Ignore(e => e.EffectiveAmounts);
        builder.Ignore(e => e.IsEnded);
    }
}

public class RecurringExpenseAmountConfiguration : IEntityTypeConfiguration<RecurringExpenseAmount>
{
    public void Configure(EntityTypeBuilder<RecurringExpenseAmount> builder)
    {
        builder.ToTable("recurring_expense_amounts", t =>
        {
            t.HasCheckConstraint("CK_recurring_expense_amounts_amount", "monthly_amount > 0");
            t.HasCheckConstraint("CK_recurring_expense_amounts_range", "effective_until IS NULL OR effective_until >= effective_from");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(a => a.RecurringExpenseId).HasColumnName("recurring_expense_id");
        builder.Property(a => a.MonthlyAmount).HasColumnName("monthly_amount").HasColumnType("numeric(12,2)");
        builder.Property(a => a.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("TRY");
        builder.Property(a => a.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(a => a.EffectiveUntil).HasColumnName("effective_until");
        builder.Property(a => a.SupersededAt).HasColumnName("superseded_at");
        builder.Property(a => a.CreatedBy).HasColumnName("created_by");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(a => new { a.RecurringExpenseId, a.EffectiveFrom });
        // Kalem başına yürürlükte (kapanmamış, düzeltmeyle geçersizleşmemiş) tek tutar olabilir -
        // tuition_rates'teki "tür başına tek açık tarife" kısıtının aynısı.
        builder.HasIndex(a => a.RecurringExpenseId)
            .IsUnique()
            .HasFilter("effective_until IS NULL AND superseded_at IS NULL")
            .HasDatabaseName("ix_recurring_expense_amounts_one_open");
    }
}
