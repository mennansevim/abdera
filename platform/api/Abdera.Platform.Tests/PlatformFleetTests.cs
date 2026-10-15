using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Platform.Tests;

// Filo güncellemesi (§7): kanarya → ~%10 → kalan; hata dağıtımı durdurur. Kendi veritabanı (ayrı fixture).
public class PlatformFleetTests(PlatformFleetTests.Fixture factory) : IClassFixture<PlatformFleetTests.Fixture>
{
    public class Fixture : PlatformFactory;

    [Fact]
    public async Task Rollout_moves_wave_by_wave_and_halts_on_failure()
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.15.1", 'a');
        await factory.AddHostAsync(client, "filo-1");
        var canary = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("kanarya", "2026.10.15.1", canary: true));
        foreach (var slug in new[] { "okul-a", "okul-b", "okul-c" })
            await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest(slug, "2026.10.15.1"));
        await factory.RunAllAsync();

        // Dalgalar: kanarya, kalan 3 okulun %10'u (en az 1), geri kalan 2.
        await using (var db = factory.Db())
        {
            var eligible = await db.Schools.Where(s => s.Status == SchoolStatus.Active).ToListAsync();
            Assert.Equal(["kanarya"], RolloutWorker.WaveMembers(eligible, 0).Select(s => s.Slug));
            Assert.Single(RolloutWorker.WaveMembers(eligible, 1));
            Assert.Equal(2, RolloutWorker.WaveMembers(eligible, 2).Count);
        }

        await factory.RegisterReleaseAsync(client, "2026.10.20.1", 'b');
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/rollouts", new { version = "2026.10.20.1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/rollouts", new { version = "2026.10.20.1" })).StatusCode);

        var rollouts = factory.Services.GetRequiredService<RolloutWorker>();
        for (var i = 0; i < 20; i++)
        {
            await rollouts.TickAsync(CancellationToken.None);
            await factory.RunAllAsync();
        }
        await using (var db = factory.Db())
        {
            Assert.Equal(RolloutStatus.Completed, (await db.Rollouts.SingleAsync()).Status);
            Assert.All(await db.Schools.ToListAsync(), s => Assert.Equal("2026.10.20.1", s.AppVersion));
            // Kanarya ilk güncellenen okul.
            var firstUpgrade = await db.Runs.Where(r => r.Kind == RunKind.Upgrade).OrderBy(r => r.CreatedAt).FirstAsync();
            Assert.Equal(canary, firstUpgrade.SchoolId);
        }

        // Bir sonraki sürümde kanarya güncellemesi başarısız: dağıtım durur, diğer okullara dokunulmaz.
        await factory.RegisterReleaseAsync(client, "2026.10.25.1", 'c');
        await client.PostAsJsonAsync("/api/rollouts", new { version = "2026.10.25.1" });
        factory.Agent.FailNext("upgrade");
        for (var i = 0; i < 5; i++)
        {
            await rollouts.TickAsync(CancellationToken.None);
            await factory.RunAllAsync();
        }
        await using (var db = factory.Db())
        {
            var rollout = await db.Rollouts.SingleAsync(r => r.Version == "2026.10.25.1");
            Assert.Equal(RolloutStatus.Halted, rollout.Status);
            Assert.Contains("kanarya", rollout.HaltReason);
            Assert.Equal(0, await db.Schools.CountAsync(s => s.AppVersion == "2026.10.25.1"));
            // Geri alınan güncellemeden sonra okul bozuk sürüme "geçiyor" görünmez.
            Assert.Null((await db.Schools.SingleAsync(s => s.Slug == "kanarya")).TargetVersion);
        }
    }
}

// Kanarya bekleme süresi (§7): işçi her turda dalgayı "tamamlandı" görür; süre ilk tamamlanmadan ölçülmeli.
// Önceki hata: tamamlanma anı her turda yenilendiği için süre hiç dolmuyor, dağıtım 0. dalgada kalıyordu.
public class PlatformCanarySoakTests(PlatformCanarySoakTests.Fixture factory) : IClassFixture<PlatformCanarySoakTests.Fixture>
{
    public class Fixture : PlatformFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Rollouts:CanarySoakMinutes"] = "60" }));
        }
    }

    [Fact]
    public async Task Rollout_leaves_the_canary_wave_once_the_soak_time_has_passed()
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.15.1", 'a');
        await factory.AddHostAsync(client, "bekleme-1");
        await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("kanarya", "2026.10.15.1", canary: true));
        await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("diger", "2026.10.15.1"));
        await factory.RunAllAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.20.1", 'b');
        await client.PostAsJsonAsync("/api/rollouts", new { version = "2026.10.20.1" });

        var rollouts = factory.Services.GetRequiredService<RolloutWorker>();
        async Task<int> TickAsync()
        {
            await rollouts.TickAsync(CancellationToken.None);
            await factory.RunAllAsync();
            await using var db = factory.Db();
            return (await db.Rollouts.SingleAsync()).Wave;
        }

        Assert.Equal(0, await TickAsync()); // kanarya güncellemesi
        Assert.Equal(0, await TickAsync()); // dalga tamamlandı, bekleme başladı
        factory.Time.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(0, await TickAsync()); // bekleme sürüyor; bu tur tamamlanma anını yenilememeli
        factory.Time.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(1, await TickAsync());
    }
}

// Kapasite ve süreli sahiplik (§6 adım 2, §16.2). Kendi veritabanı.
public class PlatformCapacityTests(PlatformCapacityTests.Fixture factory) : IClassFixture<PlatformCapacityTests.Fixture>
{
    public class Fixture : PlatformFactory;

    [Fact]
    public async Task Two_concurrent_installs_cannot_take_the_same_last_slot()
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.15.1", 'a');
        // 2048 MB → %80 = 1638 MB kapasite: 768 MB'lık iki okul sığar. Önce bir okul kurulur, tek yer kalır.
        await factory.AddHostAsync(client, "kucuk", 2048);
        await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("ilk", "2026.10.15.1"));
        await factory.RunAllAsync();

        var a = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("yaris-a", "2026.10.15.1"));
        var b = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("yaris-b", "2026.10.15.1"));
        var worker = factory.Services.GetRequiredService<ProvisioningWorker>();
        await Task.WhenAll(worker.RunNextAsync(CancellationToken.None), worker.RunNextAsync(CancellationToken.None));
        await factory.RunAllAsync();

        await using var db = factory.Db();
        var statuses = await db.Schools.Where(s => s.Id == a || s.Id == b).Select(s => s.Status).ToListAsync();
        Assert.Contains(SchoolStatus.Active, statuses);
        Assert.Contains(SchoolStatus.Failed, statuses);
        var host = await db.Hosts.SingleAsync();
        Assert.Equal(2 * 768, host.ReservedMemoryMb);
        Assert.True(host.ReservedMemoryMb <= host.CapacityMb);
    }
}

// Çöken turun devralınması (§16.2). Kendi veritabanı.
public class PlatformTakeoverTests(PlatformTakeoverTests.Fixture factory) : IClassFixture<PlatformTakeoverTests.Fixture>
{
    public class Fixture : PlatformFactory;

    [Fact]
    public async Task Crashed_worker_step_is_taken_over_without_running_the_command_twice()
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.15.1", 'a');
        await factory.AddHostAsync(client, "genis");
        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("devral", "2026.10.15.1"));

        // Önceki sahip "İmajları çek" (adım 5) ajanı çalıştırdı ama sonucu kaydedemeden öldü.
        var worker = factory.Services.GetRequiredService<ProvisioningWorker>();
        await using (var db = factory.Db())
        {
            var run = await db.Runs.SingleAsync(r => r.SchoolId == id);
            var school = await db.Schools.SingleAsync(s => s.Id == id);
            var host = await db.Hosts.OrderBy(h => h.ReservedMemoryMb).FirstAsync(h => h.Name == "genis");
            host.Reserve(768, DateTimeOffset.UtcNow);
            school.AssignHost(host.Id, DateTimeOffset.UtcNow);
            run.Claim(factory.Time.GetUtcNow().AddHours(-1), TimeSpan.FromMinutes(1));
            run.Advance(5, factory.Time.GetUtcNow().AddHours(-1), TimeSpan.FromMinutes(1));
            db.Steps.Add(ProvisioningStep.Start(run.Id, 5, "İmajları çek", 1, factory.Time.GetUtcNow().AddHours(-1)));
            await db.SaveChangesAsync();
            await factory.Agent.RunAsync(host, Abdera.Platform.Api.Infrastructure.AgentCommand.For("pull", "devral", operationId: run.OperationId(5, 1)), CancellationToken.None);
        }
        var pulls = factory.Agent.Calls.Count(c => c.Contains("pull devral"));

        await factory.RunAllAsync();

        await using var after = factory.Db();
        Assert.Equal(SchoolStatus.Active, (await after.Schools.SingleAsync(s => s.Id == id)).Status);
        // Sahte ajan aynı işlem kimliğini yeniden oynattı: pull ikinci kez çalışmadı.
        Assert.Equal(pulls, factory.Agent.Calls.Count(c => c.Contains("pull devral")));
        Assert.Equal(1, await after.Steps.CountAsync(s => s.Step == 5 && s.Status == StepStatus.Succeeded && s.Attempt == 1));
    }
}
