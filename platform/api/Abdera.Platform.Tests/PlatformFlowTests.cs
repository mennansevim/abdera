using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Platform.Tests;

// docs/multi-tenant/ARCHITECTURE.md §3-§7, §16: platformun uçtan uca akışları (sahte ajanla).
public class PlatformFlowTests(PlatformFactory factory) : IClassFixture<PlatformFactory>
{
    private async Task<HttpClient> ReadyAsync(string version = "2026.10.15.1", char digit = 'a')
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, version, digit);
        await using var db = factory.Db();
        if (!await db.Hosts.AnyAsync()) await factory.AddHostAsync(client, "hel-1");
        return client;
    }

    private async Task<School> SchoolAsync(Guid id)
    {
        await using var db = factory.Db();
        return await db.Schools.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private async Task<ProvisioningRun> LatestRunAsync(Guid id, RunKind? kind = null)
    {
        await using var db = factory.Db();
        return await db.Runs.AsNoTracking().Where(r => r.SchoolId == id && (kind == null || r.Kind == kind))
            .OrderByDescending(r => r.CreatedAt).FirstAsync();
    }

    [Fact]
    public async Task Without_totp_school_management_is_forbidden()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/schools")).StatusCode);
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = PlatformFactory.OperatorEmail, password = "yanlis-parola-123", totpCode = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var operatorClient = await factory.OperatorAsync();
        Assert.Equal(HttpStatusCode.OK, (await operatorClient.GetAsync("/api/schools")).StatusCode);

        // TOTP etkin: kodsuz giriş reddedilir; aynı kod ikinci kez kabul edilmez.
        var noCode = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = PlatformFactory.OperatorEmail, password = PlatformFactory.OperatorPassword, totpCode = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, noCode.StatusCode);
        factory.Time.Advance(TimeSpan.FromSeconds(31));
        var code = Totp.CodeAt(factory.Secret, Totp.StepAt(factory.Time.GetUtcNow()));
        var first = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = PlatformFactory.OperatorEmail, password = PlatformFactory.OperatorPassword, totpCode = code });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var replay = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = PlatformFactory.OperatorEmail, password = PlatformFactory.OperatorPassword, totpCode = code });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Release_must_use_digest_pinned_images()
    {
        var client = await factory.OperatorAsync();
        var bad = await client.PostAsJsonAsync("/api/releases", new
        {
            version = "2026.10.15.7", apiImage = "ghcr.io/abdera/abdera-api:latest", webImage = PlatformFactory.Image("abdera-web", 'f'),
            revision = (string?)null, modules = PlatformFactory.Catalog, notes = (string?)null,
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Wizard_creates_school_and_provisioning_runs_every_step_to_active()
    {
        var client = await ReadyAsync();
        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("deniz", "2026.10.15.1", ["progress", "whatsapp"],
            new Dictionary<string, string> { ["WhatsApp__AccessToken"] = "meta-token-gizli" }));

        await factory.RunAllAsync();

        var school = await SchoolAsync(id);
        Assert.Equal(SchoolStatus.Active, school.Status);
        Assert.Equal("2026.10.15.1", school.AppVersion);
        Assert.Equal("203.0.113.10", factory.Dns.Records["deniz.abdera.test"]);

        var calls = factory.Agent.Calls.Where(c => c.Contains(" deniz")).ToList();
        string[] expected = ["create deniz", "pull deniz", "up deniz", "status deniz", "provision deniz", "route add deniz deniz.abdera.test", "backup-now deniz"];
        Assert.Equal(expected, calls.Select(c => string.Join(' ', c.Split(' ').Skip(1).TakeWhile(part => part != "--op"))).Where(c => expected.Contains(c)).Distinct());

        // .env okulun sözleşmesine uyar; WhatsApp sırları eksik olduğu için sağlayıcı kapalı yazılır.
        var env = factory.Agent.Stdins.First(stdin => stdin.Contains("INSTANCE_SLUG=deniz"));
        Assert.Contains("Modules__Enabled=progress,whatsapp", env);
        Assert.Contains("WhatsApp__Provider=Disabled", env);
        Assert.Contains($"ABDERA_API_IMAGE={PlatformFactory.Image("abdera-api", 'a')}", env);
        Assert.Contains("Alerts__WebhookUrl=https://platform.abdera.test/api/alerts", env);
        Assert.Contains("Frontend__Origin=https://deniz.abdera.test", env);

        // Kurulum isteği okulun InstanceProvisioning sözleşmesinde ve platformun ürettiği geçici parolayı taşır.
        var provision = JsonDocument.Parse(factory.Agent.Stdins.First(stdin => stdin.Contains("\"temporaryPassword\"") && stdin.Contains("mudur@deniz.test"))).RootElement;
        Assert.Equal("mudur@deniz.test", provision.GetProperty("admin").GetProperty("email").GetString());
        Assert.Equal("Individual", provision.GetProperty("tariffs")[0].GetProperty("courseKind").GetString());

        // Detay cevabı sır taşımaz; parola yalnızca açıkça istenince ve audit'le döner.
        var detailRaw = await client.GetStringAsync($"/api/schools/{id}");
        Assert.DoesNotContain("meta-token-gizli", detailRaw);
        Assert.DoesNotContain(provision.GetProperty("admin").GetProperty("temporaryPassword").GetString()!, detailRaw);
        var reveal = await (await client.PostAsync($"/api/schools/{id}/reveal-admin-password", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(provision.GetProperty("admin").GetProperty("temporaryPassword").GetString(), reveal.GetProperty("temporaryPassword").GetString());

        await using var db = factory.Db();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.SchoolId == id && a.Action == "school.admin_password_revealed"));
        Assert.False(await db.SchoolSecrets.AnyAsync(s => s.SchoolId == id && s.Name == SecretNames.ProvisionRequest));
        Assert.All(await db.SchoolSecrets.Where(s => s.SchoolId == id).ToListAsync(), secret => Assert.DoesNotContain("meta-token-gizli", secret.Ciphertext));
        Assert.DoesNotContain((await db.AuditLogs.Where(a => a.SchoolId == id).ToListAsync()).Select(a => a.AfterJson ?? ""), json => json.Contains("meta-token-gizli"));
        var host = await db.Hosts.SingleAsync(h => h.Name == "hel-1");
        Assert.True(host.ReservedMemoryMb >= 768);
    }

    [Fact]
    public async Task Failed_step_is_retried_from_where_it_stopped()
    {
        var client = await ReadyAsync();
        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("ada", "2026.10.15.1"));
        factory.Agent.FailNext("up");
        await factory.RunAllAsync();

        Assert.Equal(SchoolStatus.Failed, (await SchoolAsync(id)).Status);
        var failed = await LatestRunAsync(id);
        Assert.Equal(RunStatus.Failed, failed.Status);
        Assert.Contains("Başlat", failed.Error);
        var pullsBefore = factory.Agent.Calls.Count(c => c.Contains("pull ada"));

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/schools/{id}/retry", null)).StatusCode);
        await factory.RunAllAsync();

        Assert.Equal(SchoolStatus.Active, (await SchoolAsync(id)).Status);
        // Önceki adımlar yeniden çalışmadı; "Başlat" iki deneme kaydı taşır.
        Assert.Equal(pullsBefore, factory.Agent.Calls.Count(c => c.Contains("pull ada")));
        await using var db = factory.Db();
        Assert.Equal(2, await db.Steps.CountAsync(s => s.RunId == failed.Id && s.Name == "Başlat"));
    }

    [Fact]
    public async Task Cancelling_a_failed_install_cleans_up_and_releases_capacity()
    {
        var client = await ReadyAsync();
        int Reserved() { using var db = factory.Db(); return db.Hosts.Single(h => h.Name == "hel-1").ReservedMemoryMb; }
        var before = Reserved();
        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("yarim", "2026.10.15.1"));
        factory.Agent.FailNext("provision");
        await factory.RunAllAsync();
        Assert.Equal(SchoolStatus.Failed, (await SchoolAsync(id)).Status);
        Assert.Equal(before + 768, Reserved());

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/schools/{id}/cancel", null)).StatusCode);
        await factory.RunAllAsync();

        Assert.Equal(SchoolStatus.Destroyed, (await SchoolAsync(id)).Status);
        Assert.Equal(before, Reserved());
        Assert.False(factory.Dns.Records.ContainsKey("yarim.abdera.test"));
        Assert.Contains(factory.Agent.Calls, c => c.Contains("purge yarim"));
    }

    [Fact]
    public async Task Only_one_open_operation_per_school()
    {
        var client = await ReadyAsync();
        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("tek", "2026.10.15.1"));
        // Kurulum henüz yürütülmedi: ikinci bir işlem açılamaz.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/schools/{id}/suspend", null)).StatusCode);
        await factory.RunAllAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/schools/{id}/suspend", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/schools/{id}/drill", null)).StatusCode);
        await factory.RunAllAsync();
        Assert.Equal(SchoolStatus.Suspended, (await SchoolAsync(id)).Status);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/schools/{id}/resume", null)).StatusCode);
        await factory.RunAllAsync();
        Assert.Equal(SchoolStatus.Active, (await SchoolAsync(id)).Status);
    }

    [Fact]
    public async Task Destroy_is_two_stage_and_purge_goes_through_the_agent()
    {
        var client = await ReadyAsync();
        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("veda", "2026.10.15.1"));
        await factory.RunAllAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/schools/{id}/purge", null)).StatusCode);

        await client.PostAsync($"/api/schools/{id}/destroy", null);
        await factory.RunAllAsync();
        Assert.Equal(SchoolStatus.PendingDestroy, (await SchoolAsync(id)).Status);

        // Ajan 30 gün dolmadan reddeder: platform bekleyişi kısaltamaz, okul olduğu gibi kalır.
        factory.Agent.FailNext("purge");
        await client.PostAsync($"/api/schools/{id}/purge", null);
        await factory.RunAllAsync();
        Assert.Equal(SchoolStatus.PendingDestroy, (await SchoolAsync(id)).Status);
        Assert.Equal(RunStatus.Failed, (await LatestRunAsync(id, RunKind.Purge)).Status);

        await client.PostAsync($"/api/schools/{id}/undestroy", null);
        await factory.RunAllAsync();
        Assert.Equal(SchoolStatus.Active, (await SchoolAsync(id)).Status);
    }

    [Fact]
    public async Task Upgrade_only_moves_forward_and_records_the_version()
    {
        var client = await ReadyAsync();
        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("ileri", "2026.10.15.1"));
        await factory.RunAllAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.20.1", 'b');

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/schools/{id}/upgrade", new { version = "2026.10.15.1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync($"/api/schools/{id}/upgrade", new { version = "2026.10.20.1" })).StatusCode);
        await factory.RunAllAsync();

        Assert.Equal("2026.10.20.1", (await SchoolAsync(id)).AppVersion);
        Assert.Contains(factory.Agent.Calls, c => c.Contains($"upgrade ileri {PlatformFactory.Image("abdera-api", 'b')} {PlatformFactory.Image("abdera-web", 'b')}"));
    }

    [Fact]
    public async Task Module_dependencies_are_enforced_and_changes_reconfigure_the_school()
    {
        var client = await ReadyAsync();
        var missingDependency = await client.PostAsJsonAsync("/api/schools", PlatformFactory.SchoolRequest("eksik", "2026.10.15.1", ["profitability"]));
        Assert.Equal(HttpStatusCode.BadRequest, missingDependency.StatusCode);
        var unknownIntegration = await client.PostAsJsonAsync("/api/schools", PlatformFactory.SchoolRequest("tuhaf", "2026.10.15.1", ["progress"],
            new Dictionary<string, string> { ["ConnectionStrings__Default"] = "Host=evil" }));
        Assert.Equal(HttpStatusCode.BadRequest, unknownIntegration.StatusCode);

        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("modul", "2026.10.15.1", []));
        await factory.RunAllAsync();
        Assert.Contains("Modules__Enabled=none", factory.Agent.Stdins.First(s => s.Contains("INSTANCE_SLUG=modul")));

        var change = await client.PutAsJsonAsync($"/api/schools/{id}/modules", new { modules = new[] { "finance", "profitability" }, integrations = (object?)null });
        Assert.Equal(HttpStatusCode.Accepted, change.StatusCode);
        await factory.RunAllAsync();
        Assert.Contains(factory.Agent.Stdins, s => s.Contains("INSTANCE_SLUG=modul") && s.Contains("Modules__Enabled=finance,profitability"));
        Assert.Equal(["finance", "profitability"], (await SchoolAsync(id)).Modules);
    }

    [Fact]
    public async Task Hosts_without_room_stop_the_install()
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.15.1", 'a');
        await using (var db = factory.Db())
        {
            foreach (var host in await db.Hosts.ToListAsync()) host.SetStatus(HostStatus.Draining, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        try
        {
            var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("dolu", "2026.10.15.1"));
            await factory.RunAllAsync();
            var run = await LatestRunAsync(id);
            Assert.Equal(RunStatus.Failed, run.Status);
            Assert.Contains("yer yok", run.Error);
        }
        finally
        {
            await using var db = factory.Db();
            foreach (var host in await db.Hosts.ToListAsync()) host.SetStatus(HostStatus.Active, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task School_alerts_must_carry_the_school_signature()
    {
        var client = await ReadyAsync();
        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("alarm", "2026.10.15.1"));
        await factory.RunAllAsync();
        string secret;
        await using (var scope = factory.Services.CreateAsyncScope())
            secret = (await scope.ServiceProvider.GetRequiredService<SecretVault>().GetAsync(id, SecretNames.AlertsSecret))!;

        var body = JsonSerializer.Serialize(new { instance = "alarm", version = "2026.10.15.1", kind = "backup_failed", level = "Degraded", detail = "SFTP kapalı", at = DateTimeOffset.UtcNow });
        HttpRequestMessage Request(string signature)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/alerts") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-Abdera-Signature", signature);
            return request;
        }
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request("sha256=00"))).StatusCode);
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.SendAsync(Request(signature))).StatusCode);

        var alerts = await client.GetFromJsonAsync<JsonElement>("/api/alerts?open=true");
        Assert.Contains(alerts.EnumerateArray(), a => a.GetProperty("slug").GetString() == "alarm" && a.GetProperty("detail").GetString() == "SFTP kapalı");
    }

    // F5: var olan okul (bugünkü canlı okul) yeni sunucuya dökümüyle taşınır; kurulum ucu çağrılmaz.
    [Fact]
    public async Task Existing_school_is_adopted_from_its_dump_without_provisioning()
    {
        var client = await ReadyAsync();
        var bad = await client.PostAsJsonAsync("/api/schools/adopt", new
        {
            slug = "eski", displayName = "Eski Okul", shortName = "Eski", primaryColor = (string?)null, customDomain = "panel.eskiokul.com",
            version = "2026.10.15.1", modules = new[] { "progress" }, dumpName = "../../etc/passwd", integrations = (object?)null, isCanary = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var response = await client.PostAsJsonAsync("/api/schools/adopt", new
        {
            slug = "eski", displayName = "Eski Okul", shortName = "Eski", primaryColor = (string?)null, customDomain = "panel.eskiokul.com",
            version = "2026.10.15.1", modules = new[] { "progress" }, dumpName = "eski-2026-10-15.sql.gz", integrations = (object?)null, isCanary = true,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await factory.RunAllAsync();

        var school = await SchoolAsync(id);
        Assert.Equal(SchoolStatus.Active, school.Status);
        Assert.True(school.IsCanary);
        Assert.Contains(factory.Agent.Calls, c => c.Contains("restore eski eski-2026-10-15.sql.gz"));
        Assert.DoesNotContain(factory.Agent.Calls, c => c.Contains("provision eski"));
        // Özel alan adı: DNS'e dokunulmaz, okul kendi kaydını yönlendirir.
        Assert.False(factory.Dns.Records.ContainsKey("panel.eskiokul.com"));
        Assert.Contains(factory.Agent.Calls, c => c.Contains("route add eski panel.eskiokul.com"));
    }

    // Genel yapı (§5.6): kategori sürümün kataloğundan, alt birimler kurulum isteğiyle okula gider.
    [Fact]
    public async Task School_is_created_with_categories_and_branches_from_the_release_catalog()
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.15.1", 'a');
        await factory.AddHostAsync(client, "brans-1");

        var categories = await client.GetFromJsonAsync<JsonElement>("/api/releases/2026.10.15.1/categories");
        Assert.Equal("Dans türü", categories.EnumerateArray().Single(c => c.GetProperty("key").GetString() == "dance").GetProperty("unitLabel").GetString());

        // Gövde JsonNode ile genişletilir (iç içe nesneyle biten isteği metinle kesmek bozuk JSON üretir).
        string Body(object branches, string? customCategories = null)
        {
            var node = JsonSerializer.SerializeToNode(PlatformFactory.SchoolRequest("adim-dans", "2026.10.15.1"), PlatformFactory.Json)!.AsObject();
            node["branches"] = JsonSerializer.SerializeToNode(branches);
            if (customCategories is not null) node["customCategories"] = System.Text.Json.Nodes.JsonNode.Parse(customCategories);
            return node.ToJsonString();
        }
        async Task<HttpResponseMessage> PostAsync(object branches) =>
            await client.PostAsync("/api/schools", new StringContent(Body(branches), System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(new[] { new { category = "magic", name = "Uçan halı" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(new[] { new { category = "dance", name = "Bale" }, new { category = "dance", name = "bale" } })).StatusCode);

        // Katalogda olmayan alan: istekte tanımlanmadan kullanılamaz.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(new[] { new { category = "c-yoga", name = "Hatha yoga" } })).StatusCode);

        var custom = "[{\"key\":\"c-yoga\",\"name\":\"Yoga\",\"unitLabel\":\"Seans\"}]";
        var body = Body(new object[] { new { category = "dance", name = "Bale" }, new { category = "DANCE", name = "Hip hop" }, new { category = "other", name = "Yaratıcı drama" }, new { category = "c-yoga", name = "Hatha yoga" } }, custom);
        var created = await client.PostAsync("/api/schools", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await using var db = factory.Db();
        var school = await db.Schools.SingleAsync(s => s.Id == id);
        Assert.Equal(["c-yoga", "dance", "other"], school.Categories);
        Assert.Equal("Yoga", school.CustomCategoryNames["c-yoga"]);
        using var scope = factory.Services.CreateScope();
        var request = await scope.ServiceProvider.GetRequiredService<SecretVault>().GetAsync(id, SecretNames.ProvisionRequest);
        var branches = JsonDocument.Parse(request!).RootElement.GetProperty("branches");
        Assert.Equal(["dance:Bale", "dance:Hip hop", "other:Yaratıcı drama", "c-yoga:Hatha yoga"],
            branches.EnumerateArray().Select(b => $"{b.GetProperty("category").GetString()}:{b.GetProperty("name").GetString()}"));
        var customSent = JsonDocument.Parse(request!).RootElement.GetProperty("categories");
        Assert.Equal("Seans", customSent[0].GetProperty("unitLabel").GetString());
    }
}
