using Abdera.Api.Modules.Banking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Banking.Persistence;

public class BankIncomingTransactionConfiguration : IEntityTypeConfiguration<BankIncomingTransaction>
{
    public void Configure(EntityTypeBuilder<BankIncomingTransaction> builder)
    {
        builder.ToTable("bank_incoming_transactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.VirtualIbanId).HasColumnName("virtual_iban_id");
        builder.Property(t => t.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(t => t.ProviderTransactionId).HasColumnName("provider_transaction_id").HasMaxLength(200).IsRequired();
        builder.Property(t => t.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
        builder.Property(t => t.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(t => t.SenderName).HasColumnName("sender_name").HasMaxLength(200);
        builder.Property(t => t.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(t => t.ReceivedAt).HasColumnName("received_at");
        builder.Property(t => t.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.MatchedReceivableId).HasColumnName("matched_receivable_id");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        // docs/12-bank-integration.md idempotency özeti: sağlayıcı aynı bildirimi tekrar
        // gönderse de tek kayıt.
        builder.HasIndex(t => new { t.Provider, t.ProviderTransactionId }).IsUnique();
        builder.HasIndex(t => t.Status);
        builder.ToTable(tb => tb.HasCheckConstraint("CK_bank_incoming_transactions_amount", "amount > 0"));
    }
}
