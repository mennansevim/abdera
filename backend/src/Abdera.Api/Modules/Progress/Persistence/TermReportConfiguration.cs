using Abdera.Api.Modules.Progress.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Progress.Persistence;

public class TermReportConfiguration : IEntityTypeConfiguration<TermReport>
{
    public void Configure(EntityTypeBuilder<TermReport> builder)
    {
        builder.ToTable("term_reports");
        builder.HasKey(report => report.Id);
        builder.Property(report => report.Id).HasColumnName("id");
        builder.Property(report => report.StudentId).HasColumnName("student_id");
        builder.Property(report => report.EnrollmentId).HasColumnName("enrollment_id");
        builder.Property(report => report.TeacherId).HasColumnName("teacher_id");
        builder.Property(report => report.Term).HasColumnName("term").HasMaxLength(12).IsRequired();
        builder.Property(report => report.Comment).HasColumnName("comment").HasMaxLength(TermReport.CommentMaxLength);
        builder.Property(report => report.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(report => report.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("jsonb");
        builder.Property(report => report.PublishedAt).HasColumnName("published_at");
        builder.Property(report => report.PublishedByUserId).HasColumnName("published_by_user_id");
        builder.Property(report => report.CreatedAt).HasColumnName("created_at");
        builder.Property(report => report.UpdatedAt).HasColumnName("updated_at");
        // Öğretmen ve yönetici aynı karneyi aynı anda düzenleyebilir (CLAUDE.md: eşzamanlı
        // düzenleme riski olan tabloda optimistic concurrency).
        builder.Property<uint>("Version").IsRowVersion();

        // Kurs kaydı başına dönemde tek karne.
        builder.HasIndex(report => new { report.EnrollmentId, report.Term }).IsUnique();
        builder.HasIndex(report => report.StudentId);
        builder.HasIndex(report => new { report.TeacherId, report.Term });
        // Yayınlanmış karne yorumsuz ve dondurulmuş verisiz olamaz.
        builder.ToTable(table => table.HasCheckConstraint("ck_term_reports_published_complete",
            "status <> 'Published' OR (comment IS NOT NULL AND snapshot_json IS NOT NULL AND published_at IS NOT NULL)"));

        builder.HasOne<Abdera.Api.Modules.People.Domain.Student>().WithMany()
            .HasForeignKey(report => report.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Abdera.Api.Modules.People.Domain.Enrollment>().WithMany()
            .HasForeignKey(report => report.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Abdera.Api.Modules.People.Domain.Teacher>().WithMany()
            .HasForeignKey(report => report.TeacherId).OnDelete(DeleteBehavior.Restrict);
    }
}
