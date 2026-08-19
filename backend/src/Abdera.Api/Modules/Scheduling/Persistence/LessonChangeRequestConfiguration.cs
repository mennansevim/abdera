using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Scheduling.Persistence;

public class LessonChangeRequestConfiguration : IEntityTypeConfiguration<LessonChangeRequest>
{
    public void Configure(EntityTypeBuilder<LessonChangeRequest> builder)
    {
        builder.ToTable("lesson_change_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.LessonId).HasColumnName("lesson_id");
        builder.Property(r => r.RequestedBy).HasColumnName("requested_by");
        builder.Property(r => r.Reason).HasColumnName("reason");
        builder.Property(r => r.ProposedStartAt).HasColumnName("proposed_start_at");
        builder.Property(r => r.ProposedEndAt).HasColumnName("proposed_end_at");
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.ResolvedAt).HasColumnName("resolved_at");

        builder.HasIndex(r => r.LessonId);
        builder.HasIndex(r => r.Status);

        builder.ToTable(t => t.HasCheckConstraint("CK_lesson_change_requests_time_range", "proposed_end_at > proposed_start_at"));
    }
}
