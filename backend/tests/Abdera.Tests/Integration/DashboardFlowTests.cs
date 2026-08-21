using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Dashboard.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Modules.Scheduling.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Tests.Integration;

// docs/00-master-prompt.md "Dashboard" bölümü + docs/07-api.md GET /api/dashboard/today
// (denetim ARC-6/E2, docs/13-audit-fix-prompt.md madde 13). docs/04-permissions.md: Admin
// okul geneli, Teacher yalnızca kendi dersleri özetini görür, mali alanlar Teacher'a hiç
// görünmez.
public class DashboardFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public DashboardFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"));
        response.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Dashboard_counts_are_scoped_by_role()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        const string teacherAEmail = "dashboard-teacher-a@test.local";
        var teacherA = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("DashboardA", "Teacher", [piano.Id], teacherAEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;
        var teacherB = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("DashboardB", "Teacher", [piano.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;

        // Teacher A'nın öğrencisinin doğum günü 5 gün sonra - 30 günlük pencere içinde net,
        // UTC/yerel saat sınırındaki bir kayma yüzünden testin kırılgan olmasını önler.
        var upcomingBirthday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var studentA = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("StudentA", "Dash", new DateOnly(upcomingBirthday.Year - 10, upcomingBirthday.Month, upcomingBirthday.Day))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var studentB = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("StudentB", "Dash", new DateOnly(2014, 6, 15))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        var guardianA = (await (await admin.PostAsJsonAsync("/api/guardians",
                new Guardians.CreateRequest("GuardianA", "Dash", "05551230001")))
            .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;
        await admin.PostAsJsonAsync($"/api/students/{studentA.Id}/guardians",
            new LinkGuardianToStudent.Request(guardianA.Id, "anne", true));

        var enrollmentA = (await (await admin.PostAsJsonAsync($"/api/students/{studentA.Id}/enrollments",
                new Enrollments.CreateRequest(teacherA.Teacher.Id, piano.Id, new DateOnly(2026, 1, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
        await admin.PostAsJsonAsync($"/api/students/{studentB.Id}/enrollments",
            new Enrollments.CreateRequest(teacherB.Id, piano.Id, new DateOnly(2026, 1, 1)));

        // lessonA NORMAL durumunda olmalı (değişiklik talebi yalnızca NORMAL'de açılabilir,
        // bkz. ChangeRequests.CreateAsync) - bunun için gerçek bir LessonSeries'e bağlanıyor.
        // Haftalık seri üretim akışını tetiklemiyoruz (gün eşleşmesi gerektirir), doğrudan
        // "bugün"e Lesson.CreateFromSeries ile açıyoruz. lessonB ise MAKEUP - farklı bir
        // durumun da dashboard'a doğru sayıldığını (Cancelled olmadığı sürece) gösteriyor.
        //
        // Ders saatleri artık `now.AddHours(n)` ile DEĞİL, okulun yerel "bugün"üne sabit
        // saatlerle (10:00/12:00) çapalanıyor - Dashboard.cs "bugün" penceresini
        // Europe/Istanbul yerel gün sınırına göre hesaplıyor (docs/00-master-prompt.md).
        // Eski hâliyle test UTC gece yarısına yakın (İstanbul akşam saatlerinde) çalışınca
        // `now.AddHours(3..4)` yerel günü aşıp "yarın"a düşüyor, TodayLessons beklenenin altına
        // düşüp testi kırılgan (flaky) yapıyordu - gerçek bir prod bug'ı bulundu ve CI'da
        // gözlemlendi.
        var now = DateTimeOffset.UtcNow;
        var clock = _factory.Services.GetRequiredService<IClock>();
        var todayLocal = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var lessonAStart = LessonGenerator.ToUtcInstant(todayLocal, new TimeOnly(10, 0), clock.SchoolTimeZone);
        var lessonAEnd = lessonAStart.AddMinutes(45);
        var lessonBStart = LessonGenerator.ToUtcInstant(todayLocal, new TimeOnly(12, 0), clock.SchoolTimeZone);
        var lessonBEnd = lessonBStart.AddMinutes(45);
        var seriesA = Abdera.Api.Modules.Scheduling.Domain.LessonSeries.Create(
            enrollmentA.Id, clock.ToSchoolLocal(lessonAStart).DayOfWeek, TimeOnly.FromDateTime(lessonAStart.DateTime), 45,
            todayLocal, null, now);
        db.LessonSeries.Add(seriesA);
        var lessonA = Lesson.CreateFromSeries(seriesA.Id, studentA.Id, teacherA.Teacher.Id, piano.Id, lessonAStart, lessonAEnd, now);
        var lessonB = Lesson.CreateMakeup(studentB.Id, teacherB.Id, piano.Id, lessonBStart, lessonBEnd, now);
        db.Lessons.AddRange(lessonA, lessonB);

        var rsvp = LessonRsvp.Create(lessonA.Id, guardianA.Id, now);
        rsvp.Respond(RsvpResponse.Attending, RsvpSource.Admin, now);
        db.LessonRsvps.Add(rsvp);
        // lessonB'ye kasıtlı olarak hiç RSVP eklenmiyor - NoResponse dalını da kapsasın diye.

        await db.SaveChangesAsync();

        var changeRequestResponse = await admin.PostAsJsonAsync($"/api/lessons/{lessonA.Id}/change-requests",
            new ChangeRequests.CreateRequest("dashboard testi", now.AddDays(2), now.AddDays(2).AddMinutes(45)));
        Assert.Equal(HttpStatusCode.Created, changeRequestResponse.StatusCode);

        // Admin: okul geneli - her iki öğretmenin de bugünkü dersini görür.
        var adminDashboard = await admin.GetFromJsonAsync<Dashboard.TodayResponse>("/api/dashboard/today", TestJson.Options);
        Assert.NotNull(adminDashboard);
        Assert.True(adminDashboard!.TodayLessons >= 2);
        Assert.True(adminDashboard.Attending >= 1);
        Assert.True(adminDashboard.NoResponse >= 1);
        // todayLessons her zaman üç RSVP kovasının toplamına eşit olmalı.
        Assert.Equal(adminDashboard.TodayLessons, adminDashboard.Attending + adminDashboard.NotAttending + adminDashboard.NoResponse);
        Assert.True(adminDashboard.PendingChangeRequests >= 1);
        Assert.True(adminDashboard.UpcomingBirthdays >= 1);

        // Teacher A: yalnızca kendi dersi - CLAUDE.md/docs/04-permissions.md rol izolasyonu.
        using var teacherAClient = _factory.CreateClient();
        var teacherLogin = await teacherAClient.PostAsJsonAsync("/api/auth/login",
            new Login.Request(teacherAEmail, teacherA.TemporaryPassword!));
        Assert.Equal(HttpStatusCode.OK, teacherLogin.StatusCode);

        var teacherDashboard = await teacherAClient.GetFromJsonAsync<Dashboard.TodayResponse>("/api/dashboard/today", TestJson.Options);
        Assert.NotNull(teacherDashboard);
        Assert.Equal(1, teacherDashboard!.TodayLessons);
        Assert.Equal(1, teacherDashboard.Attending);
        Assert.Equal(0, teacherDashboard.NotAttending);
        Assert.Equal(0, teacherDashboard.NoResponse);
        // Mali özet Teacher'a hiç görünmez (docs/04-permissions.md) - okulda başka overdue
        // aidat olsa bile burada her zaman 0.
        Assert.Equal(0, teacherDashboard.OverduePayments);
    }
}
