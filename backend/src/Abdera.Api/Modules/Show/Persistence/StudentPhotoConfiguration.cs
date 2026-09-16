using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Show.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Show.Persistence;

public class StudentPhotoConfiguration : IEntityTypeConfiguration<StudentPhoto>
{
    public void Configure(EntityTypeBuilder<StudentPhoto> builder)
    {
        builder.ToTable("student_photos");
        builder.HasKey(p => p.StudentId);
        builder.Property(p => p.StudentId).HasColumnName("student_id");
        builder.Property(p => p.ContentType).HasColumnName("content_type").HasMaxLength(50);
        builder.Property(p => p.Content).HasColumnName("content");
        builder.Property(p => p.Version).HasColumnName("version");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        // Öğrenci silinirse fotoğrafı da gider - kişisel veri, tutmanın bir gerekçesi yok
        // (mali kayıtların aksine). Öğrenci silme akışı için bkz. Students.cs DeleteAsync.
        builder.HasOne<Student>().WithMany()
            .HasForeignKey(p => p.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}
