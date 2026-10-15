using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Infrastructure;
using Abdera.Api.Modules.Scheduling.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Abdera.Tests.Integration;

// NotificationDispatcher'i kapali bir host: "henuz gonderilmemis" job'larin iptalini sinayan
// testler, 1 saniyelik dispatcher'la yarisa girmeden job'i PENDING'de tutabilsin diye.
// Eskiden test job'i olustuktan SONRA scheduled_at'i ileri otelemeye calisiyordu; dispatcher
// arada davranirsa job Sent oluyordu (CI'da kararsizdi).
public class NoDispatcherWebApplicationFactory : AbderaWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            var dispatcher = services.Single(d =>
                d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(NotificationDispatcher));
            services.Remove(dispatcher);
        });
    }
}

public class PendingScheduleMessageFlowTests : IClassFixture<NoDispatcherWebApplicationFactory>
{
    private readonly NoDispatcherWebApplicationFactory _factory;

    public PendingScheduleMessageFlowTests(NoDispatcherWebApplicationFactory factory)
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
    public async Task Moving_a_series_again_cancels_the_unsent_message_about_the_previous_schedule()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await MessagingFlowTests.SeedLessonAsync(admin, "prgiki");
        var seriesId = (await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == seeded.LessonId)).LessonSeriesId!.Value;

        var first = (await (await admin.PostAsJsonAsync($"/api/lesson-series/{seriesId}/reschedule",
                new LessonSeriesFeatures.RescheduleRequest(DayOfWeek.Thursday, new TimeOnly(17, 0), 45, null)))
            .Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;
        var firstJobBefore = await db.NotificationJobs.AsNoTracking().SingleAsync(j =>
            j.Type == NotificationJobType.LessonScheduleChanged && j.ReferenceId == first.Series.Id);
        Assert.Equal(NotificationJobStatus.Pending, firstJobBefore.Status);

        var secondResponse = await admin.PostAsJsonAsync($"/api/lesson-series/{first.Series.Id}/reschedule",
            new LessonSeriesFeatures.RescheduleRequest(DayOfWeek.Friday, new TimeOnly(17, 0), 45, null));
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = (await secondResponse.Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;

        var firstJob = await db.NotificationJobs.AsNoTracking().SingleAsync(j => j.Id == firstJobBefore.Id);
        Assert.Equal(NotificationJobStatus.Cancelled, firstJob.Status);
        Assert.True(await db.NotificationJobs.AnyAsync(j =>
            j.Type == NotificationJobType.LessonScheduleChanged && j.ReferenceId == second.Series.Id));
    }
}
