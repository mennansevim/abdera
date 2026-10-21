using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Tests;

// docs/multi-tenant/ARCHITECTURE.md §5.7: kullanıcı paketi. Fiyatın ekseni personel hesabı sayısı;
// paket okul satırına donar, sınır okulun .env'ine yazılır, yalnızca sınır değişince okul yeniden yapılandırılır.
public class PlatformPackageTests(PlatformFactory factory) : IClassFixture<PlatformFactory>
{
    private async Task<HttpClient> ReadyAsync()
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.15.1", 'a');
        await using var db = factory.Db();
        if (!await db.Hosts.AnyAsync()) await factory.AddHostAsync(client, "paket-1");
        return client;
    }

    private async Task<School> SchoolAsync(Guid id)
    {
        await using var db = factory.Db();
        return await db.Schools.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private static StringContent WithPackage(string slug, string[] modules, object? package)
    {
        var body = JsonSerializer.SerializeToNode(PlatformFactory.SchoolRequest(slug, "2026.10.15.1", modules), PlatformFactory.Json)!.AsObject();
        body["package"] = package is null ? null : JsonSerializer.SerializeToNode(package, PlatformFactory.Json);
        return new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
    }

    [Fact]
    public async Task Wizard_school_needs_a_package_and_the_price_is_frozen_on_the_school()
    {
        var client = await ReadyAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/schools", WithPackage("paketsiz", ["progress"], null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/schools",
            WithPackage("sifir", ["progress"], new { staffSeats = 0, pricePerSeat = 400m, addonPrices = new Dictionary<string, decimal>() }))).StatusCode);
        // Seçilmeyen eklentiye fiyat verilemez; negatif ve üç ondalıklı fiyat reddedilir.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/schools",
            WithPackage("yanlis", ["progress"], new { staffSeats = 5, pricePerSeat = 400m, addonPrices = new Dictionary<string, decimal> { ["whatsapp"] = 100m } }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/schools",
            WithPackage("eksi", ["progress"], new { staffSeats = 5, pricePerSeat = -1m, addonPrices = new Dictionary<string, decimal>() }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/schools",
            WithPackage("kurus", ["progress"], new { staffSeats = 5, pricePerSeat = 1.005m, addonPrices = new Dictionary<string, decimal>() }))).StatusCode);

        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("paketli", "2026.10.15.1", ["progress", "whatsapp"]));
        var school = await SchoolAsync(id);
        Assert.Equal(8, school.StaffSeatLimit);
        Assert.Equal(400m, school.PricePerSeat);
        Assert.Equal(8 * 400m + 100m + 100m, school.MonthlyTotal);

        var tiles = await client.GetFromJsonAsync<JsonElement>("/api/schools");
        var package = tiles.EnumerateArray().Single(t => t.GetProperty("slug").GetString() == "paketli").GetProperty("package");
        Assert.Equal(3400m, package.GetProperty("monthlyTotal").GetDecimal());
        Assert.Equal("TRY", package.GetProperty("currency").GetString());

        await factory.RunAllAsync();
        Assert.Contains(factory.Agent.Stdins, s => s.Contains("INSTANCE_SLUG=paketli") && s.Contains("School__StaffSeatLimit=8\n"));
    }

    [Fact]
    public async Task Seat_change_reconfigures_the_school_but_a_price_change_does_not()
    {
        var client = await ReadyAsync();
        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("buyuyen", "2026.10.15.1", ["progress"]));
        await factory.RunAllAsync();
        Assert.Equal(SchoolStatus.Active, (await SchoolAsync(id)).Status);

        var priceOnly = await client.PutAsJsonAsync($"/api/schools/{id}/package",
            new { staffSeats = 8, pricePerSeat = 350m, addonPrices = new Dictionary<string, decimal> { ["progress"] = 0m } });
        Assert.Equal(HttpStatusCode.OK, priceOnly.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await priceOnly.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").ValueKind);
        await using (var db = factory.Db())
            Assert.False(await db.Runs.AnyAsync(r => r.SchoolId == id && r.Kind == RunKind.Reconfigure));
        Assert.Equal(8 * 350m, (await SchoolAsync(id)).MonthlyTotal);

        var grow = await client.PutAsJsonAsync($"/api/schools/{id}/package",
            new { staffSeats = 12, pricePerSeat = 350m, addonPrices = new Dictionary<string, decimal> { ["progress"] = 0m } });
        Assert.Equal(HttpStatusCode.OK, grow.StatusCode);
        await factory.RunAllAsync();
        Assert.Contains(factory.Agent.Stdins, s => s.Contains("INSTANCE_SLUG=buyuyen") && s.Contains("School__StaffSeatLimit=12\n"));
        Assert.Equal(12 * 350m, (await SchoolAsync(id)).MonthlyTotal);

        await using (var db = factory.Db())
        {
            var audit = await db.AuditLogs.AsNoTracking().Where(a => a.SchoolId == id && a.Action == "school.package_changed").CountAsync();
            Assert.Equal(2, audit);
        }
    }

    [Fact]
    public async Task Turning_an_add_on_on_uses_the_list_price_and_turning_it_off_drops_its_price()
    {
        var client = await ReadyAsync();
        var id = await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("eklentili", "2026.10.15.1", ["progress"]));
        await factory.RunAllAsync();

        // Test ortamında liste fiyatı tanımlı değil: yeni eklenti 0 ile girer, operatör Paket sekmesinden fiyatlar.
        var change = await client.PutAsJsonAsync($"/api/schools/{id}/modules", new { modules = new[] { "whatsapp" }, integrations = (object?)null });
        Assert.Equal(HttpStatusCode.Accepted, change.StatusCode);
        var school = await SchoolAsync(id);
        Assert.Equal(["whatsapp"], school.AddonPrices.Keys);
        Assert.Equal(8 * 400m, school.MonthlyTotal);

        var pricing = await client.GetFromJsonAsync<JsonElement>("/api/pricing");
        Assert.Equal("TRY", pricing.GetProperty("currency").GetString());
    }
}
