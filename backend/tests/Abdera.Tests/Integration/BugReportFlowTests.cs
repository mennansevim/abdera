using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Features;
using Abdera.Api.Modules.People.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// "Hata bildir" (docs/10-decisions.md W): öğretmen gönderir ve unutur, liste ve durum
// yalnızca yöneticide. Liste uç noktası HTTP üzerinden gerçekten çağrılır (CLAUDE.md OrderBy kuralı).
public class BugReportFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public BugReportFlowTests(AbderaWebApplicationFactory factory)
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

    private Task<HttpClient> CreateAdminClientAsync() => LoginAsync("admin@test.local", "Test1234!");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Beklenmeyen durum: {(int)response.StatusCode} {response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private async Task<HttpClient> CreateTeacherClientAsync(HttpClient admin, string suffix)
    {
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(instrument => instrument.Code == "PIANO");
        var email = $"bildirim.{suffix}@test.local";
        var created = await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/teachers", new Teachers.CreateRequest($"Bildirim{suffix}", "Ogretmen", [piano.Id], email)));
        return await LoginAsync(email, created.TemporaryPassword!);
    }

    [Fact]
    public async Task Teacher_report_reaches_admin_list_and_can_be_triaged_to_an_issue()
    {
        var admin = await CreateAdminClientAsync();
        var teacher = await CreateTeacherClientAsync(admin, "mutlu");

        var created = await ReadAsync<BugReports.CreateResponse>(await teacher.PostAsJsonAsync(
            "/api/bug-reports",
            new BugReports.CreateRequest(BugReportKind.Bug, "/dashboard/billing", "  Tahsil et deyince ekran donuyor.  ", "2026.10.10.1")));

        var list = await ReadAsync<BugReports.ListResponse>(await admin.GetAsync("/api/bug-reports?status=New"));
        var item = Assert.Single(list.Page.Items, report => report.Id == created.Id);
        Assert.Equal("Tahsil et deyince ekran donuyor.", item.Description);
        Assert.Equal("/dashboard/billing", item.PagePath);
        Assert.Equal("BildirimMutlu Ogretmen", item.ReporterName, ignoreCase: true);
        Assert.Equal("2026.10.10.1", item.AppVersion);
        Assert.True(list.Counts.New >= 1);

        var triage = await admin.PatchAsJsonAsync(
            $"/api/bug-reports/{created.Id}", new BugReports.TriageRequest(BugReportStatus.Triaged, 14, null));
        Assert.Equal(HttpStatusCode.NoContent, triage.StatusCode);

        await using var db = await _factory.CreateDbContextAsync();
        var stored = await db.BugReports.AsNoTracking().SingleAsync(report => report.Id == created.Id);
        Assert.Equal(BugReportStatus.Triaged, stored.Status);
        Assert.Equal(14, stored.GithubIssueNumber);
    }

    [Fact]
    public async Task Teacher_cannot_list_or_triage_and_short_descriptions_are_rejected()
    {
        var admin = await CreateAdminClientAsync();
        var teacher = await CreateTeacherClientAsync(admin, "yetki");

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/bug-reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PatchAsJsonAsync(
            $"/api/bug-reports/{Guid.NewGuid()}", new BugReports.TriageRequest(BugReportStatus.Rejected, null, null))).StatusCode);

        var tooShort = await teacher.PostAsJsonAsync(
            "/api/bug-reports", new BugReports.CreateRequest(BugReportKind.Suggestion, "/dashboard", "kısa", null));
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
    }

    [Fact]
    public async Task Triaging_without_an_issue_number_is_rejected()
    {
        var admin = await CreateAdminClientAsync();
        var created = await ReadAsync<BugReports.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/bug-reports", new BugReports.CreateRequest(BugReportKind.Suggestion, "/dashboard/calendar", "Haftalık görünüme öğretmen filtresi.", null)));

        var response = await admin.PatchAsJsonAsync(
            $"/api/bug-reports/{created.Id}", new BugReports.TriageRequest(BugReportStatus.Triaged, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // W6: görev canlıya çıkınca yönetici (ya da run abdera) bildirimi "Çözüldü" yapar;
    // bildirimi yapan öğretmen kendi teyit bekleyen listesinde görür ve teyit eder.
    [Fact]
    public async Task Reporter_confirms_a_resolved_report_or_sends_it_back_as_still_broken()
    {
        var admin = await CreateAdminClientAsync();
        var teacher = await CreateTeacherClientAsync(admin, "teyit");

        async Task<Guid> ResolvedReportAsync(string description, int issue)
        {
            var created = await ReadAsync<BugReports.CreateResponse>(await teacher.PostAsJsonAsync(
                "/api/bug-reports", new BugReports.CreateRequest(BugReportKind.Bug, "/dashboard/calendar", description, null)));
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync(
                $"/api/bug-reports/{created.Id}", new BugReports.TriageRequest(BugReportStatus.Triaged, issue, null))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync(
                $"/api/bug-reports/{created.Id}", new BugReports.TriageRequest(BugReportStatus.Resolved, null, null))).StatusCode);
            return created.Id;
        }

        var works = await ResolvedReportAsync("Telafi saati seçilemiyor, hep öneri yerleşiyor.", 22);
        var broken = await ResolvedReportAsync("Takvimde sürükleyince ders kayboluyor.", 24);

        var awaiting = await ReadAsync<List<BugReports.BugReportResponse>>(await teacher.GetAsync("/api/bug-reports/awaiting-confirmation"));
        Assert.Equal([broken, works], awaiting.Select(report => report.Id).Where(id => id == works || id == broken).OrderBy(id => id == works).ToList());
        Assert.All(awaiting, report => Assert.Equal(BugReportStatus.Resolved, report.Status));
        Assert.Equal(22, awaiting.Single(report => report.Id == works).GithubIssueNumber);

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.PostAsJsonAsync(
            $"/api/bug-reports/{works}/confirmation", new BugReports.ConfirmRequest(true, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.PostAsJsonAsync(
            $"/api/bug-reports/{broken}/confirmation", new BugReports.ConfirmRequest(false, "  Hâlâ kayboluyor, bu sefer cuma günü.  "))).StatusCode);

        var remaining = await ReadAsync<List<BugReports.BugReportResponse>>(await teacher.GetAsync("/api/bug-reports/awaiting-confirmation"));
        Assert.DoesNotContain(remaining, report => report.Id == works || report.Id == broken);

        var verifiedOrReopened = await ReadAsync<BugReports.ListResponse>(
            await admin.GetAsync("/api/bug-reports?status=Verified&status=Reopened&pageSize=100"));
        Assert.Equal(BugReportStatus.Verified, verifiedOrReopened.Page.Items.Single(report => report.Id == works).Status);
        var reopened = verifiedOrReopened.Page.Items.Single(report => report.Id == broken);
        Assert.Equal(BugReportStatus.Reopened, reopened.Status);
        Assert.Equal("Hâlâ kayboluyor, bu sefer cuma günü.", reopened.FeedbackNote);
        Assert.Equal(24, reopened.GithubIssueNumber);
        Assert.True(verifiedOrReopened.Counts.Verified >= 1 && verifiedOrReopened.Counts.Reopened >= 1);
    }

    [Fact]
    public async Task Only_resolved_reports_can_be_confirmed_and_only_by_their_reporter()
    {
        var admin = await CreateAdminClientAsync();
        var reporter = await CreateTeacherClientAsync(admin, "sahip");
        var stranger = await CreateTeacherClientAsync(admin, "yabanci");

        var created = await ReadAsync<BugReports.CreateResponse>(await reporter.PostAsJsonAsync(
            "/api/bug-reports", new BugReports.CreateRequest(BugReportKind.Bug, "/dashboard", "Ana ekranda sayaç yanlış.", null)));

        // Göreve dönüşmemiş bildirim çözülemez, çözülmemiş bildirim teyit edilemez.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync(
            $"/api/bug-reports/{created.Id}", new BugReports.TriageRequest(BugReportStatus.Resolved, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await reporter.PostAsJsonAsync(
            $"/api/bug-reports/{created.Id}/confirmation", new BugReports.ConfirmRequest(true, null))).StatusCode);
        // Teyit durumları ilk triage'dan verilemez.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync(
            $"/api/bug-reports/{created.Id}", new BugReports.TriageRequest(BugReportStatus.Verified, 30, null))).StatusCode);

        await admin.PatchAsJsonAsync($"/api/bug-reports/{created.Id}", new BugReports.TriageRequest(BugReportStatus.Triaged, 30, null));
        await admin.PatchAsJsonAsync($"/api/bug-reports/{created.Id}", new BugReports.TriageRequest(BugReportStatus.Resolved, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync(
            $"/api/bug-reports/{created.Id}/confirmation", new BugReports.ConfirmRequest(true, null))).StatusCode);
        Assert.DoesNotContain(
            await ReadAsync<List<BugReports.BugReportResponse>>(await stranger.GetAsync("/api/bug-reports/awaiting-confirmation")),
            report => report.Id == created.Id);

        await using var db = await _factory.CreateDbContextAsync();
        var stored = await db.BugReports.AsNoTracking().SingleAsync(report => report.Id == created.Id);
        Assert.Equal(BugReportStatus.Resolved, stored.Status);
        Assert.Equal(30, stored.GithubIssueNumber);
        Assert.NotNull(stored.ResolvedAt);
    }
}
