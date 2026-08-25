using Abdera.Api.Modules.Progress.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Progress.Persistence;

public class SkillAssessmentConfiguration : IEntityTypeConfiguration<SkillAssessment>
{
    public void Configure(EntityTypeBuilder<SkillAssessment> builder)
    {
        builder.ToTable("skill_assessments", table =>
            table.HasCheckConstraint("ck_skill_assessments_score", "score BETWEEN 1 AND 5"));
        builder.HasKey(assessment => assessment.Id);
        builder.Property(assessment => assessment.Id).HasColumnName("id");
        builder.Property(assessment => assessment.StudentId).HasColumnName("student_id");
        builder.Property(assessment => assessment.SkillDefinitionId).HasColumnName("skill_definition_id");
        builder.Property(assessment => assessment.TeacherId).HasColumnName("teacher_id");
        builder.Property(assessment => assessment.LessonId).HasColumnName("lesson_id");
        builder.Property(assessment => assessment.Score).HasColumnName("score");
        builder.Property(assessment => assessment.Note).HasColumnName("note").HasMaxLength(1000);
        builder.Property(assessment => assessment.AssessedAt).HasColumnName("assessed_at");

        builder.HasIndex(assessment => new { assessment.StudentId, assessment.AssessedAt });
        builder.HasOne<Abdera.Api.Modules.People.Domain.Student>().WithMany()
            .HasForeignKey(assessment => assessment.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SkillDefinition>().WithMany()
            .HasForeignKey(assessment => assessment.SkillDefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Abdera.Api.Modules.People.Domain.Teacher>().WithMany()
            .HasForeignKey(assessment => assessment.TeacherId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Abdera.Api.Modules.Scheduling.Domain.Lesson>().WithMany()
            .HasForeignKey(assessment => assessment.LessonId).OnDelete(DeleteBehavior.Restrict);
    }
}
