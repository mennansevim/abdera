using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Profitability.Domain;
using Abdera.Api.Modules.Profitability.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Abdera.Tests.Integration;

// Kârlılık sekmesi gerçek cookie auth + Minimal API + PostgreSQL üzerinden. Özet sorgusu çok
// tabloyu okuyor; CLAUDE.md'nin "en az bir kez HTTP üzerinden çağır" kuralı burada uygulanıyor.
public class ProfitabilityFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public ProfitabilityFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Overview_counts_completed_lessons_at_the_teacher_rate_and_lists_operation_gaps()
    {
        var seeded = await SeedTeachingAsync("Kârlıoğlu");
        var admin = await CreateAdminClientAsync();

        var response = await admin.GetAsync("/api/profitability");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Beklenmeyen durum: {response.StatusCode}, gövde: {body}");
        var overview = (await response.Content.ReadFromJsonAsync<Profitability.OverviewResponse>(TestJson.Options))!;

        Assert.Equal(ProfitabilitySnapshotBuilder.MonthsShown, overview.Months.Count);
        var current = overview.Months[^1];
        Assert.True(current.IsCurrent);
        Assert.True(current.TeacherCost >= 500m, $"Öğretmen maliyeti: {current.TeacherCost}");
        Assert.Equal(500m, overview.Unit.AverageTeacherRate);

        var instrument = overview.Instruments.Single(row => row.Id == seeded.InstrumentId);
        Assert.True(instrument.Individual >= 1);
        Assert.Equal(1m, instrument.BookedHours);
        Assert.Equal(10m, instrument.AvailableHours);
        // Kayıt doğrudan veritabanına yazıldı, bu ayın aidatı açılmadı: eksik olarak görünmeli.
        Assert.Contains(overview.Gaps, gap => gap.Key == "enrollments_without_receivable");
        Assert.NotEmpty(overview.Insights);
    }

    [Fact]
    public async Task Idea_is_saved_evaluated_with_school_data_and_can_be_removed()
    {
        var seeded = await SeedTeachingAsync("Fikiroğlu");
        var admin = await CreateAdminClientAsync();
        var request = new Profitability.IdeaRequest(GrowthIdeaKind.GroupClass, "  Piyano grubu  ", null,
            seeded.InstrumentId, null, null, 4, null, null, null, null, null, null, null, null);

        var preview = await admin.PostAsJsonAsync("/api/profitability/ideas/preview", request with { Title = null });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewed = (await preview.Content.ReadFromJsonAsync<Profitability.EvaluationResponse>(TestJson.Options))!;
        Assert.Equal(12, previewed.Series.Count);

        var createResponse = await admin.PostAsJsonAsync("/api/profitability/ideas", request);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.True(createResponse.StatusCode == HttpStatusCode.Created, $"Beklenmeyen durum: {createResponse.StatusCode}, gövde: {createBody}");
        var created = (await createResponse.Content.ReadFromJsonAsync<Profitability.IdeaResponse>(TestJson.Options))!;
        Assert.Equal("Piyano grubu", created.Title);
        Assert.Equal(previewed.MonthlyNet, created.Evaluation.MonthlyNet);

        (await admin.PostAsJsonAsync($"/api/profitability/ideas/{created.Id}/status",
            new Profitability.StatusRequest(GrowthIdeaStatus.Trying))).EnsureSuccessStatusCode();
        var overview = await admin.GetFromJsonAsync<Profitability.OverviewResponse>("/api/profitability", TestJson.Options);
        Assert.Equal(GrowthIdeaStatus.Trying, overview!.Ideas.Single(idea => idea.Id == created.Id).Status);

        (await admin.DeleteAsync($"/api/profitability/ideas/{created.Id}")).EnsureSuccessStatusCode();
        await using var db = await _factory.CreateDbContextAsync();
        Assert.False(await db.GrowthIdeas.AnyAsync(idea => idea.Id == created.Id));
    }

    [Fact]
    public async Task One_person_group_is_rejected_and_nothing_is_written()
    {
        var seeded = await SeedTeachingAsync("Tekoğlu");
        var admin = await CreateAdminClientAsync();
        await using var before = await _factory.CreateDbContextAsync();
        var count = await before.GrowthIdeas.CountAsync();

        var response = await admin.PostAsJsonAsync("/api/profitability/ideas", new Profitability.IdeaRequest(
            GrowthIdeaKind.GroupClass, "Tek kişilik grup", null, seeded.InstrumentId, null, null, 1, null, null, null, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var after = await _factory.CreateDbContextAsync();
        Assert.Equal(count, await after.GrowthIdeas.CountAsync());
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/api/profitability");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Commentary_is_generated_once_a_month_from_nameless_facts_and_refresh_is_capped()
    {
        await SeedTeachingAsync("Gizlioğlu");
        await ClearCommentariesAsync();
        var generator = new FakeCommentaryGenerator();
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IProfitCommentaryGenerator>();
            services.AddSingleton<IProfitCommentaryGenerator>(generator);
        }));
        var admin = await CreateAdminClientAsync(factory);

        var first = await GetCommentaryAsync(admin);
        var second = await GetCommentaryAsync(admin);

        Assert.Equal(ProfitCommentaries.CommentaryStatus.Ready, first.Status);
        Assert.Equal("Yorum #1", first.Text);
        Assert.Equal("Yorum #1", second.Text);
        Assert.Single(generator.Facts);
        Assert.DoesNotContain("Gizlioğlu", generator.Facts[0]);
        Assert.Equal(ProfitCommentary.MaxRefreshesPerMonth, first.RefreshesLeft);

        for (var i = 0; i < ProfitCommentary.MaxRefreshesPerMonth; i++)
            (await admin.PostAsync("/api/profitability/commentary/refresh", null)).EnsureSuccessStatusCode();
        var capped = await admin.PostAsync("/api/profitability/commentary/refresh", null);

        Assert.Equal(HttpStatusCode.Conflict, capped.StatusCode);
        // Hak dolunca sağlayıcıya hiç gidilmez.
        Assert.Equal(1 + ProfitCommentary.MaxRefreshesPerMonth, generator.Facts.Count);
        Assert.Equal(0, (await GetCommentaryAsync(admin)).RefreshesLeft);
    }

    [Fact]
    public async Task Commentary_without_a_provider_reports_unavailable_and_writes_nothing()
    {
        await ClearCommentariesAsync();
        var admin = await CreateAdminClientAsync();

        var commentary = await GetCommentaryAsync(admin);

        Assert.Equal(ProfitCommentaries.CommentaryStatus.Unavailable, commentary.Status);
        Assert.Null(commentary.Text);
        await using var db = await _factory.CreateDbContextAsync();
        Assert.False(await db.ProfitCommentaries.AnyAsync());
    }

    private record Seeded(Guid TeacherId, Guid InstrumentId);

    // Öğretmen (ders başı 500, pazartesi 10 saat müsait) + öğrenci + birebir kayıt + haftalık
    // 1 saatlik program + bu ay tamamlanmış bir ders. Her test kendi enstrümanını açar ki
    // aynı sınıftaki testlerin sayıları birbirine karışmasın.
    private async Task<Seeded> SeedTeachingAsync(string lastName)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var code = $"T{Guid.NewGuid():N}"[..12];
        var instrument = Instrument.Create($"Branş {code}", code);
        var teacher = Teacher.Create("Öğretmen", lastName, now);
        var student = Student.Create("Öğrenci", lastName, new DateOnly(2014, 1, 1), now);
        db.AddRange(instrument, teacher, student);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var enrollment = Enrollment.Create(student.Id, teacher.Id, instrument.Id, CourseKind.Individual, today, now);
        db.AddRange(
            TeacherInstrument.Create(teacher.Id, instrument.Id),
            TeacherPayRate.Create(teacher.Id, 500m, "TRY", null, now),
            TeacherAvailability.Create(teacher.Id, DayOfWeek.Monday, new TimeOnly(10, 0), new TimeOnly(20, 0)),
            enrollment);
        await db.SaveChangesAsync();

        var series = LessonSeries.Create(enrollment.Id, DayOfWeek.Monday, new TimeOnly(10, 0), 60, today, null, now);
        db.Add(series);
        await db.SaveChangesAsync();

        // Ayın ilk gününe yakın bir an: hangi gün koşulursa koşulsun bu ayın içinde kalır.
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 12, 0, 0, TimeSpan.Zero);
        var lesson = Lesson.CreateFromSeries(series.Id, student.Id, teacher.Id, instrument.Id, monthStart, monthStart.AddHours(1), now);
        lesson.Complete(now);
        db.Add(lesson);
        await db.SaveChangesAsync();
        return new Seeded(teacher.Id, instrument.Id);
    }

    private async Task ClearCommentariesAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.ProfitCommentaries.ExecuteDeleteAsync();
    }

    private static async Task<ProfitCommentaries.Response> GetCommentaryAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/profitability/commentary");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Beklenmeyen durum: {response.StatusCode}, gövde: {body}");
        return (await response.Content.ReadFromJsonAsync<ProfitCommentaries.Response>(TestJson.Options))!;
    }

    private async Task<HttpClient> CreateAdminClientAsync(WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? _factory).CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private sealed class FakeCommentaryGenerator : IProfitCommentaryGenerator
    {
        private readonly List<string> _facts = [];

        public bool IsAvailable => true;
        public string ModelName => "fake-model";
        public IReadOnlyList<string> Facts { get { lock (_facts) return _facts.ToList(); } }

        public Task<ProfitCommentaryResult> GenerateAsync(string facts, CancellationToken cancellationToken = default)
        {
            int count;
            lock (_facts)
            {
                _facts.Add(facts);
                count = _facts.Count;
            }
            return Task.FromResult(new ProfitCommentaryResult(true, $"Yorum #{count}", null));
        }
    }
}
