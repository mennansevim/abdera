using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expenses", t => t.HasCheckConstraint("CK_expenses_amount", "amount > 0"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(160);
        builder.Property(e => e.Amount).HasColumnName("amount").HasPrecision(12, 2);
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("TRY");
        builder.Property(e => e.ExpenseDate).HasColumnName("expense_date");
        builder.Property(e => e.Note).HasColumnName("note");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(e => e.ExpenseDate);
    }
}
