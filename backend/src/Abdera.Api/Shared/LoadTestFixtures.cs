using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Shared;

// k6 yük testi (LOADTEST.md) için sentetik fixture üreticisi. DevelopmentMockData.cs'ten
// kasıtlı olarak AYRI: o sabit boyutlu bir demo hikayesi, bunu
// değiştirmek mevcut demo/test akışlarını bozar. Bu dosya yalnızca hacim üretir - CLAUDE.md
// hedef ölçeğine (6-8 öğretmen, ~150 öğrenci) yakın, tamamen sentetik veri (gerçek kullanıcı
// verisi YOK), gerçek domain factory'leri (Teacher.Create, Lesson.CreateFromSeries vb.)
// kullanarak DB invariant'larını bozmadan üretir. Yalnızca Development ortamında route
// edilir (bkz. Program.cs), AdminOnly. Şema değişikliği yapmaz - yalnızca satır ekler.
public static class LoadTestFixtures
{
    private const string EmailPrefix = "loadtest.teacher";
    private const string LoadTestPassword = "LoadTest123!";

    private const int MaximumTeacherCount = 10;
    private const int MaximumStudentCount = 150;

    // Development bootstrapper temiz veritabanına bir öğretmen ekler; dokuz yük testi
    // öğretmeniyle görünür toplam tam 10, öğrenci toplamı da en fazla 144 olur.
    public record SeedRequest(int TeacherCount = 9, int StudentsPerTeacher = 16, int HistoryWeeks = 26, int FutureWeeks = 6);

    public record SeedResponse(string Status, int Teachers, int Students, int Enrollments, int Lessons, string TeacherPassword);

    public static void MapLoadTestFixtures(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/load-test/seed", SeedAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> SeedAsync(
        LoadTestFixtures.SeedRequest? request,
        AbderaDbContext db,
        IPasswordHasher<User> passwordHasher,
        IClock clock)
    {
        var req = request ?? new SeedRequest();
        if (req.TeacherCount is < 1 or > MaximumTeacherCount ||
            req.StudentsPerTeacher < 1 ||
            (long)req.TeacherCount * req.StudentsPerTeacher > MaximumStudentCount)
        {
            return Results.BadRequest(new
            {
                message = $"Yük testi demo verisi en fazla {MaximumTeacherCount} öğretmen ve {MaximumStudentCount} öğrenci içerebilir.",
            });
        }

        var schoolZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, schoolZone).DateTime);
        var from = today.AddDays(-7 * req.HistoryWeeks);
        var until = today.AddDays(7 * req.FutureWeeks);

        var existingTeacherCount = await db.Users.CountAsync(u => u.Email.StartsWith(EmailPrefix));
        var existingStudentCount = await db.Students.CountAsync(s => s.FirstName.StartsWith("LTStudent"));
        var nonLoadTestTeacherCount = await db.Teachers.CountAsync() - existingTeacherCount;
        var nonLoadTestStudentCount = await db.Students.CountAsync() - existingStudentCount;
        if (nonLoadTestTeacherCount + req.TeacherCount > MaximumTeacherCount ||
            nonLoadTestStudentCount + (long)req.TeacherCount * req.StudentsPerTeacher > MaximumStudentCount)
        {
            return Results.BadRequest(new
            {
                message = $"Mevcut kayıtlarla birlikte demo veri en fazla {MaximumTeacherCount} öğretmen ve {MaximumStudentCount} öğrenci içerebilir.",
            });
        }

        if (existingTeacherCount >= req.TeacherCount)
        {
            return Results.Ok(new SeedResponse(
                "already-seeded",
                existingTeacherCount,
                existingStudentCount,
                await db.Enrollments.CountAsync(),
                await db.Lessons.CountAsync(),
                LoadTestPassword));
        }

        var instruments = await db.Instruments.ToListAsync();
        if (instruments.Count == 0)
        {
            throw new InvalidOperationException("Yük testi fixture'ı için enstrüman referans verisi (migration seed) bulunamadı.");
        }

        var teacherFirstNames = new[] { "Deniz", "Kaan", "Ela", "Umut", "Zeynep", "Baran", "Ceren", "Onur", "Nazlı", "Tolga", "Sude", "Yiğit" };
        var teacherLastNames = new[] { "Yıldız", "Aydınlı", "Korkmaz", "Şimşek", "Bulut", "Aksu", "Kaplan", "Tekin" };
        var studentFirstNames = new[] { "Ali", "Zehra", "Mert", "İrem", "Cem", "Naz", "Efe", "Sıla", "Arda", "Su" };

        var random = new Random(424242);
        var teachers = new List<(Teacher Teacher, Instrument Instrument)>();

        for (var t = existingTeacherCount; t < req.TeacherCount; t++)
        {
            var email = $"{EmailPrefix}.{t}@abdera.local";
            var user = User.Create(email, "placeholder", UserRole.Teacher, now);
            user.SetPassword(passwordHasher.HashPassword(user, LoadTestPassword), now);
            db.Users.Add(user);

            var teacher = Teacher.Create(teacherFirstNames[t % teacherFirstNames.Length], teacherLastNames[t % teacherLastNames.Length], now, user.Id);
            db.Teachers.Add(teacher);

            var instrument = instruments[t % instruments.Count];
            db.TeacherInstruments.Add(TeacherInstrument.Create(teacher.Id, instrument.Id));

            for (var day = 1; day <= 6; day++)
            {
                db.TeacherAvailabilities.Add(TeacherAvailability.Create(
                    teacher.Id,
                    (DayOfWeek)day,
                    new TimeOnly(14, 0),
                    new TimeOnly(21, 0)));
            }

            teachers.Add((teacher, instrument));
        }

        await db.SaveChangesAsync();

        var enrollmentCount = 0;
        var lessonCount = 0;
        var studentCount = 0;

        foreach (var (teacher, instrument) in teachers)
        {
            for (var s = 0; s < req.StudentsPerTeacher; s++)
            {
                var globalIndex = studentCount;
                var birthYear = 2010 + (globalIndex % 9);
                var student = Student.Create(
                    $"LTStudent{globalIndex:D4}",
                    studentFirstNames[globalIndex % studentFirstNames.Length],
                    new DateOnly(birthYear, 1 + (globalIndex % 12), 1 + (globalIndex % 27)),
                    now);
                db.Students.Add(student);

                var enrollment = Enrollment.Create(student.Id, teacher.Id, instrument.Id, from, now);
                db.Enrollments.Add(enrollment);
                enrollmentCount++;

                var dayOfWeek = (DayOfWeek)(1 + (globalIndex % 6));
                var startTime = new TimeOnly(14 + (globalIndex % 7), globalIndex % 2 == 0 ? 0 : 30);
                var series = LessonSeries.Create(enrollment.Id, dayOfWeek, startTime, 50, from, until, now);
                db.LessonSeries.Add(series);
                await db.SaveChangesAsync();

                var firstLessonDate = NextOnOrAfter(from, dayOfWeek);
                for (var date = firstLessonDate; date <= until; date = date.AddDays(7))
                {
                    var startAt = ToUtc(date, startTime, schoolZone);
                    var endAt = ToUtc(date, startTime.AddMinutes(50), schoolZone);
                    var lesson = Lesson.CreateFromSeries(series.Id, student.Id, teacher.Id, instrument.Id, startAt, endAt, now);
                    db.Lessons.Add(lesson);
                    lessonCount++;
                }

                studentCount++;
            }

            await db.SaveChangesAsync();
        }

        return Results.Ok(new SeedResponse(
            "seeded",
            teachers.Count,
            studentCount,
            enrollmentCount,
            lessonCount,
            LoadTestPassword));
    }

    private static DateOnly NextOnOrAfter(DateOnly from, DayOfWeek dayOfWeek)
    {
        var difference = ((int)dayOfWeek - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(difference);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }
}
