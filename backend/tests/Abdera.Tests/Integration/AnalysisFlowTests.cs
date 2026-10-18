using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Dashboard.Domain;
using Abdera.Api.Modules.Dashboard.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Tests.Integration;

// Öğrenci ve öğretmen analizi uçları: aylık şeritteki sayıların doğru kovaya düşmesi ve rol
// kapsamı (öğretmen yalnızca kendi öğrencisini/kendini görür, aidat Admin'e ait).
public class AnalysisFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public AnalysisFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request(email, password));
        response.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Analysis_buckets_last_month_lessons_and_respects_role_scope()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync("admin@test.local", "Test1234!");
        var clock = _factory.Services.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        const string teacherAEmail = "analysis-teacher-a@test.local";
        const string teacherBEmail = "analysis-teacher-b@test.local";
        var teacherA = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("AnalysisA", "Teacher", [piano.Id], teacherAEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;
        var teacherB = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("AnalysisB", "Teacher", [piano.Id], teacherBEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Analiz", "Ogrenci", new DateOnly(2015, 3, 12))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacherA.Teacher.Id, piano.Id, new DateOnly(2025, 9, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        // Dersler okulun yerel takviminde geçen aya sabitleniyor - bugünün saatine bağlı olmasın.
        var todayLocal = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        var lastMonth = new DateOnly(todayLocal.Year, todayLocal.Month, 1).AddMonths(-1);
        var lastPeriod = AnalysisTimeline.PeriodOf(lastMonth);
        DateTimeOffset At(int day) => LessonGenerator.ToUtcInstant(lastMonth.AddDays(day - 1), new TimeOnly(17, 0), clock.SchoolTimeZone);

        var series = LessonSeries.Create(enrollment.Id, clock.ToSchoolLocal(At(5)).DayOfWeek, new TimeOnly(17, 0), 45, lastMonth, null, now);
        db.LessonSeries.Add(series);
        Lesson NewLesson(int day) => Lesson.CreateFromSeries(series.Id, student.Id, teacherA.Teacher.Id, piano.Id, At(day), At(day).AddMinutes(45), now);
        var present = NewLesson(5);
        var absent = NewLesson(12);
        var excused = NewLesson(19);
        var cancelled = NewLesson(26);
        cancelled.Cancel(now);
        db.Lessons.AddRange(present, absent, excused, cancelled);
        db.LessonAttendances.AddRange(
            LessonAttendance.Create(present.Id, AttendanceStatus.Present, teacherA.Teacher.Id, null, now),
            LessonAttendance.Create(absent.Id, AttendanceStatus.Absent, teacherA.Teacher.Id, null, now),
            LessonAttendance.Create(excused.Id, AttendanceStatus.Excused, teacherA.Teacher.Id, null, now));
        db.LessonNotes.Add(LessonNote.Create(present.Id, teacherA.Teacher.Id, "Gam", "Sol el rahatladı.", null, "Tempo 80",
            "Minuet in G", 3, now, pieceComposer: "Bach", pieceStatus: RepertoireStatus.Learning));
        var assignment = PracticeAssignment.Create(present.Id, "Minuet ilk 8 ölçü", null, now);
        assignment.MarkCompleted(now);
        db.PracticeAssignments.Add(assignment);
        await db.SaveChangesAsync();

        // Admin: tüm şerit + aidat alanları.
        var adminView = await admin.GetFromJsonAsync<StudentAnalysis.Response>(
            $"/api/students/{student.Id}/analysis?months=3", TestJson.Options);
        Assert.NotNull(adminView);
        Assert.Equal(3, adminView!.Months.Count);
        Assert.Equal(AnalysisTimeline.PeriodOf(todayLocal), adminView.Months[^1].Period);
        var month = adminView.Months.Single(m => m.Period == lastPeriod);
        Assert.Equal(4, month.Lessons.Count);
        Assert.Equal(1, month.Attended);
        Assert.Equal(3, month.Rated);
        Assert.Equal(33, month.AttendanceRate);
        Assert.Equal(1, month.Cancelled);
        Assert.Equal(3.0m, month.AverageDifficulty);
        Assert.Equal(100, month.AssignmentRate);
        Assert.Equal("Sol el rahatladı.", month.LastNote?.Text);
        Assert.True(adminView.BillingVisible);
        Assert.NotNull(adminView.OpenBalance);
        var piece = Assert.Single(adminView.Pieces);
        Assert.Equal("Minuet in G", piece.Title);
        Assert.Equal(lastPeriod, piece.FirstPeriod);
        Assert.False(piece.Finished);
        var weekday = Assert.Single(adminView.Weekdays);
        Assert.Equal(1, weekday.Present + weekday.Makeup);
        Assert.Equal(1, weekday.Absent);

        // Öğretmen A: kendi öğrencisi - akademik şerit var, aidat yok.
        var teacherAClient = await LoginAsync(teacherAEmail, teacherA.TemporaryPassword!);
        var teacherView = await teacherAClient.GetFromJsonAsync<StudentAnalysis.Response>(
            $"/api/students/{student.Id}/analysis?months=3", TestJson.Options);
        Assert.False(teacherView!.BillingVisible);
        Assert.Null(teacherView.OpenBalance);
        Assert.All(teacherView.Months, m => Assert.Null(m.Billing));
        Assert.Equal(33, teacherView.Months.Single(m => m.Period == lastPeriod).AttendanceRate);

        // Öğretmen B: öğrenci ona atanmamış, A'nın analizi de onun değil.
        var teacherBClient = await LoginAsync(teacherBEmail, teacherB.TemporaryPassword!);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacherBClient.GetAsync($"/api/students/{student.Id}/analysis")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacherBClient.GetAsync($"/api/teachers/{teacherA.Teacher.Id}/analysis")).StatusCode);

        // Öğretmen A kendi analizini görür; sayılar öğrenci analiziyle aynı kurallardan çıkar.
        var own = await teacherAClient.GetFromJsonAsync<TeacherAnalysis.Response>(
            $"/api/teachers/{teacherA.Teacher.Id}/analysis?months=3", TestJson.Options);
        var ownMonth = own!.Months.Single(m => m.Period == lastPeriod);
        Assert.Equal(1, ownMonth.Present);
        Assert.Equal(1, ownMonth.Absent);
        Assert.Equal(1, ownMonth.Excused);
        Assert.Equal(1, ownMonth.Cancelled);
        Assert.Equal(33, ownMonth.AttendanceRate);
        Assert.Equal(100, ownMonth.NoteCoverage);
        Assert.Equal(1, ownMonth.ActiveStudents);
        Assert.Equal(1, own.ActiveStudentCount);
        var row = Assert.Single(own.Students);
        Assert.Equal(student.Id, row.StudentId);
        Assert.Equal(33, row.AttendanceRate);

        // Admin her öğretmeni görür.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/teachers/{teacherB.Teacher.Id}/analysis")).StatusCode);
    }
}
