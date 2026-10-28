using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Platform.Tests;

// Gözlem paneli: bütün okullar ve sunucular tek ekranda; log ve yedek listesi istek anında ajandan.
public class PlatformMonitoringTests(PlatformFactory factory) : IClassFixture<PlatformFactory>
{
    private async Task<(HttpClient Client, Guid SchoolId, Guid HostId)> ReadyAsync()
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.15.1", 'a');
        Guid hostId;
        await using (var db = factory.Db())
            hostId = await db.Hosts.Select(h => h.Id).FirstOrDefaultAsync();
        if (hostId == Guid.Empty) hostId = await factory.AddHostAsync(client, "gozlem-1");
        Guid schoolId;
        await using (var db = factory.Db())
            schoolId = await db.Schools.Where(s => s.Slug == "gozlem").Select(s => s.Id).FirstOrDefaultAsync();
        if (schoolId == Guid.Empty)
        {
            schoolId = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("gozlem", "2026.10.15.1"));
            await factory.RunAllAsync();
        }
        await factory.Services.GetRequiredService<TelemetryPoller>().PollAllAsync(CancellationToken.None);
        await factory.Services.GetRequiredService<HostStatsPoller>().PollAllAsync(CancellationToken.None);
        return (client, schoolId, hostId);
    }

    [Fact]
    public async Task Overview_shows_every_school_and_host_with_their_latest_numbers()
    {
        var (client, schoolId, hostId) = await ReadyAsync();
        var overview = await client.GetFromJsonAsync<JsonElement>("/api/overview");

        var school = overview.GetProperty("schools").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == schoolId);
        Assert.Equal("Active", school.GetProperty("status").GetString());
        Assert.Equal(3, school.GetProperty("warnings24h").GetInt32());
        Assert.Equal(4, school.GetProperty("dbConnections").GetInt32());
        Assert.Equal("20261028000000_Fake", school.GetProperty("lastMigration").GetString());
        Assert.Equal("ok", school.GetProperty("severity").GetString());

        var host = overview.GetProperty("hosts").EnumerateArray().Single(h => h.GetProperty("id").GetGuid() == hostId);
        Assert.True(host.GetProperty("reachable").GetBoolean());
        Assert.Equal(4, host.GetProperty("cpus").GetInt32());
        Assert.Equal(1, host.GetProperty("restarts").GetInt32());

        var stats = await client.GetFromJsonAsync<JsonElement>($"/api/hosts/{hostId}/stats?hours=24");
        Assert.NotEmpty(stats.GetProperty("points").EnumerateArray());
        var container = stats.GetProperty("containers").EnumerateArray().Single();
        Assert.Equal("deniz", container.GetProperty("school").GetString());
        Assert.Equal(150m, container.GetProperty("memMb").GetDecimal());
    }

    [Fact]
    public async Task Logs_are_read_on_demand_with_a_whitelist_and_every_view_is_audited()
    {
        var (client, schoolId, _) = await ReadyAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/schools/{schoolId}/logs?service=caddy")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/schools/{schoolId}/logs?service=api&since=7d")).StatusCode);

        var response = await client.GetAsync($"/api/schools/{schoolId}/logs?service=api&since=1h&tail=300");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sahte hata", await response.Content.ReadAsStringAsync());
        Assert.Contains(factory.Agent.Calls, c => c.EndsWith("logs gozlem api 1h 300"));
        await using var db = factory.Db();
        var audit = await db.AuditLogs.Where(a => a.SchoolId == schoolId && a.Action == "school.logs_viewed").ToListAsync();
        Assert.Single(audit);
        Assert.Equal("api", JsonDocument.Parse(audit[0].AfterJson!).RootElement.GetProperty("service").GetString());
    }

    [Fact]
    public async Task Backups_list_R2_and_server_dumps_newest_first()
    {
        var (client, schoolId, _) = await ReadyAsync();
        var backups = await client.GetFromJsonAsync<JsonElement>($"/api/schools/{schoolId}/backups");
        var items = backups.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal("abdera-20261028-030000.sql.enc", items[0].GetProperty("name").GetString());
        Assert.Equal("R2", items[0].GetProperty("location").GetString());
        Assert.Equal("Sunucu", items[1].GetProperty("location").GetString());
        Assert.Equal("R2 (saklama dışı)", items[2].GetProperty("location").GetString());
        Assert.NotEqual(JsonValueKind.Null, backups.GetProperty("lastSucceededAt").ValueKind);
    }
}
