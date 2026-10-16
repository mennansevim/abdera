using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Instance.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Shared.Branches;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Abdera müzik okuluna özel değil: alt birim (branş) kategorisi arayüz terimini belirler, okul kendi
// alt birimlerini yönetir (docs/multi-tenant/ARCHITECTURE.md §5.6). Kendi veritabanı.
public class BranchFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public BranchFlowTests(AbderaWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Branch_category_drives_the_ui_term_and_inactive_branches_do_not_count()
    {
        var admin = _factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();

        // Bugünkü okul: tohum enstrümanlar müzik kategorisinde, terim "Enstrüman" (canlı davranış değişmez).
        var seeded = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        Assert.All(seeded!, instrument => Assert.Equal(BranchCatalog.Music, instrument.Category));
        Assert.Equal("Enstrüman", (await ConfigAsync()).Terms.Branch);

        var created = await admin.PostAsJsonAsync("/api/instruments", new { name = "Bale", category = "dance" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ballet = (await created.Content.ReadFromJsonAsync<Instruments.InstrumentResponse>(TestJson.Options))!;
        Assert.Equal("BALE", ballet.Code);
        var config = await ConfigAsync();
        Assert.Equal("Branş", config.Terms.Branch);
        Assert.True(config.Categories.Single(c => c.Key == "dance").InUse);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/instruments", new { name = "Bale", category = "dance" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/instruments", new { name = "Uçan halı", category = "magic" })).StatusCode);

        // Pasife alınan alt birim listede kalır ama terimi etkilemez.
        var deactivated = await admin.PutAsJsonAsync($"/api/instruments/{ballet.Id}", new { name = "Bale", category = "dance", isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal("Enstrüman", (await ConfigAsync()).Terms.Branch);
        var all = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        Assert.False(all!.Single(i => i.Id == ballet.Id).IsActive);
    }

    private async Task<InstanceConfig.ConfigResponse> ConfigAsync() =>
        (await _factory.CreateClient().GetFromJsonAsync<InstanceConfig.ConfigResponse>("/api/instance/config", TestJson.Options))!;
}

// Okulun katalogda olmayan kendi alanı ("Yoga ve meditasyon"). Kendi veritabanı.
public class CustomBranchCategoryFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public CustomBranchCategoryFlowTests(AbderaWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Admin_creates_a_custom_category_and_branches_inside_it()
    {
        var admin = _factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();

        var created = await admin.PostAsJsonAsync("/api/branch-categories", new { name = "Yoga ve meditasyon", unitLabel = "Seans" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var yoga = (await created.Content.ReadFromJsonAsync<BranchCategories.CategoryResponse>(TestJson.Options))!;
        Assert.Equal("c-yoga-ve-meditasyon", yoga.Key);
        Assert.Equal("Seanslar", yoga.UnitLabelPlural);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/branch-categories", new { name = "Yoga ve meditasyon" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/branch-categories", new { name = "Dans" })).StatusCode);
        // Katalog alanı değiştirilemez.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/branch-categories/music", new { name = "Ses" })).StatusCode);

        var branch = await admin.PostAsJsonAsync("/api/instruments", new { name = "Hatha yoga", category = yoga.Key });
        Assert.Equal(HttpStatusCode.Created, branch.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/instruments", new { name = "Pilates", category = "c-olmayan-alan" })).StatusCode);

        var config = await ConfigAsync();
        var listed = config.Categories.Single(c => c.Key == yoga.Key);
        Assert.True(listed.IsCustom);
        Assert.True(listed.InUse);

        // Ad ve terim düzenlenir; alt birimi olan alan silinmez, boş alan silinir.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/branch-categories/{yoga.Key}", new { name = "Yoga", unitLabel = "Seans" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/branch-categories/{yoga.Key}")).StatusCode);
        var empty = (await (await admin.PostAsJsonAsync("/api/branch-categories", new { name = "Mutfak atölyesi", unitLabel = "Atölye" }))
            .Content.ReadFromJsonAsync<BranchCategories.CategoryResponse>(TestJson.Options))!;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/branch-categories/{empty.Key}")).StatusCode);
    }

    private async Task<InstanceConfig.ConfigResponse> ConfigAsync() =>
        (await _factory.CreateClient().GetFromJsonAsync<InstanceConfig.ConfigResponse>("/api/instance/config", TestJson.Options))!;
}

// Platform dans/drama okulu kurar: migration'ın müzik enstrümanları yerine okulun kendi alt birimleri gelir.
public class BranchProvisioningFlowTests : IClassFixture<EmptySchoolWebApplicationFactory>
{
    private readonly EmptySchoolWebApplicationFactory _factory;

    public BranchProvisioningFlowTests(EmptySchoolWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Provisioned_branches_replace_the_seeded_music_instruments()
    {
        var platform = _factory.CreateClient();
        platform.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", EmptySchoolWebApplicationFactory.ProvisioningToken);
        var request = new InstanceProvisioning.ProvisionRequest(
            new InstanceProvisioning.AdminRequest("mudur@adim.test", "Gecici-Parola-2026", null),
            new InstanceProvisioning.ProfileRequest("Adım Dans ve Drama", "Adım", null, null),
            null, null, null,
            [new("dance", "Bale"), new("dance", "Hip hop"), new("drama", "Yaratıcı drama"), new("c-yoga", "Hatha yoga")],
            [new("c-yoga", "Yoga", "Seans", null)]);

        var duplicate = request with { Branches = [new("dance", "Bale"), new("dance", "bale")] };
        Assert.Equal(HttpStatusCode.BadRequest, (await platform.PostAsJsonAsync("/api/instance/provision", duplicate)).StatusCode);
        var unknown = request with { Branches = [new("magic", "Uçan halı")] };
        Assert.Equal(HttpStatusCode.BadRequest, (await platform.PostAsJsonAsync("/api/instance/provision", request with { Categories = [] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await platform.PostAsJsonAsync("/api/instance/provision", unknown)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await platform.PostAsJsonAsync("/api/instance/provision", request)).StatusCode);

        await using var db = await _factory.CreateDbContextAsync();
        var branches = await db.Instruments.OrderBy(i => i.Name).Select(i => new { i.Name, i.Category }).ToListAsync();
        Assert.Equal(["Bale", "Hatha yoga", "Hip hop", "Yaratıcı drama"], branches.Select(b => b.Name));
        Assert.Equal(["dance", "c-yoga", "dance", "drama"], branches.Select(b => b.Category));
        Assert.Equal("Seans", (await db.CustomBranchCategories.SingleAsync()).UnitLabel);

        // Müziğe özel beceri tohumları kalkar, alan bağımsızları kalır.
        Assert.Equal(["PRACTICE_DISCIPLINE", "TECHNIQUE"], await db.SkillDefinitions.OrderBy(s => s.Code).Select(s => s.Code).ToListAsync());

        var config = await _factory.CreateClient().GetFromJsonAsync<InstanceConfig.ConfigResponse>("/api/instance/config", TestJson.Options);
        Assert.Equal("Branş", config!.Terms.Branch);
    }
}
