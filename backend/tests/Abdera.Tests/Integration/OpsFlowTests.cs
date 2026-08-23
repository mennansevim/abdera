using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Faz 4 (docs/15-product-phases.md): yedekleme uç noktaları + sistem sağlığı özeti. Gerçek
// pg_dump çalıştığı için (Backup:Provider=Fake sadece storage/email'i sahteler, pg_dump
// gerçek Testcontainers Postgres'ine karşı çalışır) yerel makinede/CI'da `pg_dump` binary'si
// PATH'te olmalı - docs/09-testing.md'ye bu önkoşul not düşüldü.
public class OpsFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public OpsFlowTests(AbderaWebApplicationFactory factory)
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
    public async Task Manual_backup_trigger_produces_a_succeeded_run_using_fake_storage()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        // Sınıfın paylaştığı DB'de başka testlerin (ör. sayfalama testi) kendi BackupRun
        // satırları olabilir - "en yeni" satırı StartedAt'e göre aramak yerine, tetiklemeden
        // ÖNCEKİ id kümesini alıp sonra oluşan YENİ satırı buluyoruz (sıra bağımsız).
        var idsBefore = await db.BackupRuns.Select(r => r.Id).ToListAsync();

        var triggerResponse = await admin.PostAsync("/api/backup-runs/trigger", null);
        Assert.Equal(HttpStatusCode.Accepted, triggerResponse.StatusCode);

        // Tetikleme asenkron (arka planda) tamamlanıyor - pg_dump + AES-GCM şifreleme gerçekten
        // çalıştığı için birkaç saniye sürebilir, bu yüzden kısa aralıklarla yoklanıyor.
        BackupRun? run = null;
        for (var attempt = 0; attempt < 30 && run?.Status != BackupRunStatus.Succeeded; attempt++)
        {
            await Task.Delay(500);
            run = await db.BackupRuns.AsNoTracking().Where(r => !idsBefore.Contains(r.Id)).FirstOrDefaultAsync();
        }

        Assert.NotNull(run);
        Assert.True(run!.TriggeredManually);
        Assert.True(run.Status == BackupRunStatus.Succeeded, $"Beklenmeyen durum: {run.Status}, hata: {run.ErrorMessage}"); // hata mesajı görünür kalsın diye Assert.Equal yerine
        Assert.NotNull(run.RemotePath);
        Assert.True(run.SizeBytes > 0);
        Assert.EndsWith(".sql.enc", run.RemotePath);
    }

    [Fact]
    public async Task Backup_runs_list_is_paged_and_ordered_by_most_recent_first()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        // Diğer testler (ör. manuel tetikleme testi) aynı paylaşılan factory/DB'de gerçek
        // BackupRun satırları oluşturabiliyor - "en yeni" iddiasının gerçekten doğru olması
        // için burada uzak bir GELECEK tarih kullanılıyor, aksi halde sınıf içindeki başka
        // bir testin "şu an" zaman damgalı gerçek kaydı bunu geçebilir (sıra bağımlılığı).
        var farFuture = DateTimeOffset.UtcNow.AddYears(1);
        var older = BackupRun.Start(triggeredManually: false, farFuture);
        older.MarkSucceeded("older.sql.enc", 100, farFuture);
        var newer = BackupRun.Start(triggeredManually: false, farFuture.AddMinutes(1));
        newer.MarkSucceeded("newer.sql.enc", 200, farFuture.AddMinutes(1));
        db.BackupRuns.AddRange(older, newer);
        await db.SaveChangesAsync();

        var response = await admin.GetAsync("/api/backup-runs?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Abdera.Api.Shared.PagedResponse<BackupRuns.BackupRunResponse>>(TestJson.Options);

        Assert.Single(body!.Items);
        Assert.Equal("newer.sql.enc", body.Items[0].RemotePath);
        Assert.True(body.TotalCount >= 2);
    }

    [Fact]
    public async Task System_health_endpoint_reflects_the_persisted_status()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var existing = await db.SystemHealthStatuses.SingleOrDefaultAsync(s => s.Id == SystemHealthStatus.SingletonId);
        if (existing is null)
        {
            existing = SystemHealthStatus.CreateDefault(DateTimeOffset.UtcNow);
            db.SystemHealthStatuses.Add(existing);
        }
        existing.Update(SystemHealthLevel.Degraded, "Test amaçlı: son yedekleme eski.", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var response = await admin.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SystemHealth.SystemHealthResponse>(TestJson.Options);

        Assert.Equal(SystemHealthLevel.Degraded, body!.Level);
        Assert.Equal("Test amaçlı: son yedekleme eski.", body.Detail);
    }

    [Fact]
    public async Task Backup_endpoints_reject_non_admin_requests()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/backup-runs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/system/health")).StatusCode);
    }
}
