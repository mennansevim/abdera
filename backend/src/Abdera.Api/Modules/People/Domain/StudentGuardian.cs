namespace Abdera.Api.Modules.People.Domain;

// docs/03-erd.md - People > student_guardians. Kompozit anahtar (studentId, guardianId) -
// bir öğrencinin birden fazla velisi, bir velinin birden fazla öğrencisi olabilir.
public class StudentGuardian
{
    public Guid StudentId { get; private set; }
    public Guid GuardianId { get; private set; }
    public string? Relationship { get; private set; }
    public bool IsPrimary { get; private set; }

    private StudentGuardian() { }

    public static StudentGuardian Create(Guid studentId, Guid guardianId, string? relationship, bool isPrimary)
    {
        return new StudentGuardian
        {
            StudentId = studentId,
            GuardianId = guardianId,
            Relationship = string.IsNullOrWhiteSpace(relationship) ? null : relationship.Trim(),
            IsPrimary = isPrimary,
        };
    }
}
