using Abdera.Api.Modules.Ops.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Ops.Persistence;

public class BugReportConfiguration : IEntityTypeConfiguration<BugReport>
{
    public void Configure(EntityTypeBuilder<BugReport> builder)
    {
        builder.ToTable("bug_reports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.PagePath).HasColumnName("page_path").HasMaxLength(200);
        builder.Property(r => r.Description).HasColumnName("description").HasMaxLength(BugReport.MaxDescriptionLength);
        builder.Property(r => r.UserAgent).HasColumnName("user_agent").HasMaxLength(500);
        builder.Property(r => r.AppVersion).HasColumnName("app_version").HasMaxLength(40);
        builder.Property(r => r.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.GithubIssueNumber).HasColumnName("github_issue_number");
        builder.Property(r => r.TriageNote).HasColumnName("triage_note").HasMaxLength(1000);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(r => new { r.Status, r.CreatedAt });
    }
}
