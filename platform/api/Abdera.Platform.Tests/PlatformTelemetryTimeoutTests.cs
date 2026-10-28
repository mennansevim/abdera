using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Provisioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Platform.Tests;

// HttpClient zaman aşımı TaskCanceledException (bir OperationCanceledException) fırlatır. Yakalama filtresi
// istisna türüne bakınca bu "durdurma" sanılıp BackgroundService'ten dışarı kaçıyor ve bütün platform
// API'sini kapatıyordu. Yanıt vermeyen okul ulaşılamaz kaydı düşmeli, tur sürmeli. Kendi veritabanı.
public class PlatformTelemetryTimeoutTests(PlatformTelemetryTimeoutTests.Fixture factory) : IClassFixture<PlatformTelemetryTimeoutTests.Fixture>
{
    public class Fixture : PlatformFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton<ISchoolClient, TimingOutSchoolClient>());
        }
    }

    public class TimingOutSchoolClient : ISchoolClient
    {
        private readonly FakeSchoolClient inner = new();

        public Task<SchoolTelemetry> GetTelemetryAsync(School school, string telemetryToken, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 15 seconds elapsing.",
                new TimeoutException());

        public Task<string?> SmokeTestAsync(School school, CancellationToken cancellationToken, string? domain = null) =>
            inner.SmokeTestAsync(school, cancellationToken, domain);
    }

    [Fact]
    public async Task A_school_that_times_out_is_marked_unreachable_instead_of_stopping_the_poller()
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.15.1", 'a');
        await factory.AddHostAsync(client, "zamanasimi-1");
        var schoolId = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("yavas", "2026.10.15.1"));
        await factory.RunAllAsync();

        await factory.Services.GetRequiredService<TelemetryPoller>().PollAllAsync(CancellationToken.None);

        await using var db = factory.Db();
        var snapshot = await db.Telemetry.Where(t => t.SchoolId == schoolId).OrderByDescending(t => t.TakenAt).FirstAsync();
        Assert.False(snapshot.Reachable);
        Assert.Contains("Timeout", snapshot.Error);
    }
}
