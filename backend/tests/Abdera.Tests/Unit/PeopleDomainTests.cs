using Abdera.Api.Modules.People.Domain;

namespace Abdera.Tests.Unit;

// Canlı QA turunda bulunan gerçek buglar: Student/Teacher/Guardian, DB'deki
// HasMaxLength(100) sınırını uygulama katmanında hiç kontrol etmiyordu - aşırı uzun bir ad
// yakalanmayan bir DbUpdateException (500) ile patlıyordu. Student.BirthDate hiç
// sınırlanmamıştı (gelecek/1500 gibi tarihler 201 ile kabul ediliyordu). Enrollment.StartedAt
// eksik gönderilirse sessizce default(DateOnly) (0001-01-01) oluyordu.
public class PeopleDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Student_create_throws_when_first_name_exceeds_max_length()
    {
        var tooLong = new string('a', 101);

        Assert.Throws<ArgumentException>(() => Student.Create(tooLong, "Soyad", new DateOnly(2015, 1, 1), Now));
    }

    [Fact]
    public void Student_create_allows_name_at_exactly_max_length()
    {
        var atLimit = new string('a', 100);

        var student = Student.Create(atLimit, "Soyad", new DateOnly(2015, 1, 1), Now);

        Assert.Equal(atLimit, student.FirstName);
    }

    [Fact]
    public void Student_create_throws_when_birth_date_is_in_the_future()
    {
        Assert.Throws<ArgumentException>(() => Student.Create("Ad", "Soyad", new DateOnly(2099, 1, 1), Now));
    }

    [Fact]
    public void Student_create_throws_when_birth_date_is_unreasonably_old()
    {
        Assert.Throws<ArgumentException>(() => Student.Create("Ad", "Soyad", new DateOnly(1500, 1, 1), Now));
    }

    [Fact]
    public void Student_create_accepts_todays_date_as_birth_date()
    {
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        var student = Student.Create("Ad", "Soyad", today, Now);

        Assert.Equal(today, student.BirthDate);
    }

    [Fact]
    public void Student_update_throws_when_birth_date_is_in_the_future()
    {
        var student = Student.Create("Ad", "Soyad", new DateOnly(2015, 1, 1), Now);

        Assert.Throws<ArgumentException>(() => student.Update("Ad", "Soyad", new DateOnly(2099, 1, 1), Now));
    }

    [Fact]
    public void Teacher_create_throws_when_last_name_exceeds_max_length()
    {
        var tooLong = new string('a', 101);

        Assert.Throws<ArgumentException>(() => Teacher.Create("Ad", tooLong, Now));
    }

    [Fact]
    public void Guardian_create_throws_when_first_name_exceeds_max_length()
    {
        var tooLong = new string('a', 101);

        Assert.Throws<ArgumentException>(() => Guardian.Create(tooLong, "Soyad", "+905551112233", Now));
    }

    [Fact]
    public void Enrollment_create_throws_when_started_at_is_missing()
    {
        Assert.Throws<ArgumentException>(() => Enrollment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CourseKind.Individual, default, Now));
    }

    [Fact]
    public void Enrollment_create_accepts_a_real_started_at_date()
    {
        var enrollment = Enrollment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CourseKind.Group, new DateOnly(2026, 1, 1), Now);

        Assert.Equal(new DateOnly(2026, 1, 1), enrollment.StartedAt);
        Assert.Equal(CourseKind.Group, enrollment.CourseKind);
    }
}
