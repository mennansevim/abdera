using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class StudentDeletionRequestConfiguration : IEntityTypeConfiguration<StudentDeletionRequest>
{
    public void Configure(EntityTypeBuilder<StudentDeletionRequest> builder)
    {
        builder.ToTable("student_deletion_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.StudentId).HasColumnName("student_id");
        builder.Property(r => r.RequestedBy).HasColumnName("requested_by");
        builder.Property(r => r.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.DecidedBy).HasColumnName("decided_by");
        builder.Property(r => r.DecisionNote).HasColumnName("decision_note").HasMaxLength(500);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.ResolvedAt).HasColumnName("resolved_at");

        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.StudentId);

        // Aynı öğrenci için aynı anda yalnızca BİR bekleyen talep olabilir - aksi halde
        // yönetici aynı silme için üst üste karar vermek zorunda kalırdı.
        builder.HasIndex(r => r.StudentId)
            .IsUnique()
            .HasFilter("status = 'Pending'")
            .HasDatabaseName("ix_student_deletion_requests_one_pending");

        // Öğrenci silinince (onay sonrası veya doğrudan) talebi de gider.
        builder.HasOne<Student>().WithMany()
            .HasForeignKey(r => r.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}
