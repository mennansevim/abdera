using Abdera.Api.Modules.Profitability.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Profitability.Persistence;

public class ProfitCommentaryConfiguration : IEntityTypeConfiguration<ProfitCommentary>
{
    public void Configure(EntityTypeBuilder<ProfitCommentary> builder)
    {
        builder.ToTable("profit_commentaries");
        builder.HasKey(commentary => commentary.Id);
        builder.Property(commentary => commentary.Id).HasColumnName("id");
        builder.Property(commentary => commentary.Period).HasColumnName("period").HasMaxLength(7).IsRequired();
        builder.Property(commentary => commentary.Text).HasColumnName("text").HasMaxLength(ProfitCommentary.TextMaxLength).IsRequired();
        builder.Property(commentary => commentary.Model).HasColumnName("model").HasMaxLength(100).IsRequired();
        builder.Property(commentary => commentary.RefreshCount).HasColumnName("refresh_count");
        builder.Property(commentary => commentary.CreatedAt).HasColumnName("created_at");
        builder.Property(commentary => commentary.UpdatedAt).HasColumnName("updated_at");
        // Yenileme sayacı eşzamanlı iki "yenile" ile limiti aşmasın.
        builder.Property<uint>("Version").IsRowVersion();

        // Ay başına tek yorum: iki sekmenin eşzamanlı ilk açılışı iki AI çağrısını kalıcılaştıramaz.
        builder.HasIndex(commentary => commentary.Period).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_profit_commentaries_refresh_count", $"refresh_count BETWEEN 0 AND {ProfitCommentary.MaxRefreshesPerMonth}");
        });
    }
}
