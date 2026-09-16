using Abdera.Api.Modules.Show.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Show.Persistence;

public class ShowEventConfiguration : IEntityTypeConfiguration<ShowEvent>
{
    public void Configure(EntityTypeBuilder<ShowEvent> builder)
    {
        builder.ToTable("show_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Title).HasColumnName("title").HasMaxLength(150);
        builder.Property(e => e.VenueName).HasColumnName("venue_name").HasMaxLength(150);
        builder.Property(e => e.StartsAt).HasColumnName("starts_at");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CurrentItemId).HasColumnName("current_item_id");
        builder.Property(e => e.StartedAt).HasColumnName("started_at");
        builder.Property(e => e.EndedAt).HasColumnName("ended_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.StartsAt);

        // Sahne işaretçisi iki ekrandan aynı anda ilerletilebilir (yönetici ve kulis
        // tableti). İkinci yazma birincisini sessizce ezerse gösteri sırası atlanır -
        // Receivable'daki aynı gerekçe (CLAUDE.md: eşzamanlı düzenleme riski olan tablolar).
        builder.Property<uint>("Version").IsRowVersion();
    }
}
