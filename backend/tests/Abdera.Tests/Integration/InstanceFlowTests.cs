using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Instance.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Shared.Modules;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Tests.Integration;

// docs/multi-tenant/ARCHITECTURE.md §5: okul kimliği (ad, logo) ve modül anahtarları.
// İki bekçilik: (1) hiçbir ayar yapılmamışken bugünkü kurulum birebir aynı çalışır,
// (2) kapalı bir modül oturumlu ya da oturumsuz herkese 404 döner.
public class InstanceFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private static readonly byte[] SamplePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
    private readonly AbderaWebApplicationFactory _factory;

    public InstanceFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Without_configuration_every_module_is_enabled()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var anonymous = _factory.CreateClient();

        var config = await anonymous.GetFromJsonAsync<InstanceConfig.ConfigResponse>("/api/instance/config", TestJson.Options);

        Assert.All(config!.Modules, module => Assert.True(module.Enabled, module.Key));
        Assert.Equal(ModuleCatalog.All.Count, config.Modules.Count);
        Assert.False(string.IsNullOrWhiteSpace(config.Profile.Name));

        // Varsayılan kurulumda opsiyonel modülün uçları eskisi gibi çalışır (oturumsuz → 401, 404 değil).
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/library/pieces")).StatusCode);
    }

    [Fact]
    public async Task Admin_sets_name_and_logo_and_anyone_can_read_them()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync(_factory, "admin@test.local", "Test1234!");

        var update = await admin.PutAsJsonAsync("/api/instance/profile",
            new SchoolProfileEndpoints.UpdateRequest("Do Re Mi Müzik Evi", "Do Re Mi", "#5B3FA8"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var upload = await admin.PutAsync("/api/instance/logo", LogoForm(SamplePng, claimedType: "image/png"));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        using var anonymous = _factory.CreateClient();
        var config = await anonymous.GetFromJsonAsync<InstanceConfig.ConfigResponse>("/api/instance/config", TestJson.Options);
        Assert.Equal("Do Re Mi Müzik Evi", config!.Profile.Name);
        Assert.Equal("Do Re Mi", config.Profile.ShortName);
        Assert.Equal("#5b3fa8", config.Profile.PrimaryColor);
        Assert.NotNull(config.Profile.LogoUrl);

        var logo = await anonymous.GetAsync(config.Profile.LogoUrl);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(SamplePng, await logo.Content.ReadAsByteArrayAsync());

        var actions = await db.AuditLogs.AsNoTracking()
            .Where(log => log.EntityType == "SchoolProfile")
            .Select(log => log.Action)
            .ToListAsync();
        Assert.Contains("school_profile.updated", actions);
        Assert.Contains("school_profile.logo_replaced", actions);

        var delete = await admin.DeleteAsync("/api/instance/logo");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/instance/logo")).StatusCode);
    }

    [Fact]
    public async Task Logo_that_is_not_an_image_is_rejected_even_if_it_claims_to_be_one()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync(_factory, "admin@test.local", "Test1234!");

        var response = await admin.PutAsync("/api/instance/logo", LogoForm("<svg onload=alert(1)>"u8.ToArray(), claimedType: "image/png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_change_the_school_identity()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync(_factory, "admin@test.local", "Test1234!");
        var teacher = await CreateTeacherClientAsync(admin);

        var response = await teacher.PutAsJsonAsync("/api/instance/profile",
            new SchoolProfileEndpoints.UpdateRequest("Başka Okul", "Başka", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_module_returns_404_to_everyone_while_core_keeps_working()
    {
        await using var db = await _factory.CreateDbContextAsync();
        using var schoolWithoutLibrary = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EnabledModules.ConfigurationKey] = "progress,banking",
            })));

        using var anonymous = schoolWithoutLibrary.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/library/pieces")).StatusCode);

        var admin = await LoginAsync(schoolWithoutLibrary, "admin@test.local", "Test1234!");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/library/pieces")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/shows")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/profitability")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/instruments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/bank-transactions")).StatusCode);

        var config = await anonymous.GetFromJsonAsync<InstanceConfig.ConfigResponse>("/api/instance/config", TestJson.Options);
        Assert.False(config!.Modules.Single(module => module.Key == ModuleKeys.Library).Enabled);
        Assert.True(config.Modules.Single(module => module.Key == ModuleKeys.Banking).Enabled);
        Assert.True(config.Modules.Single(module => module.Key == ModuleKeys.Billing).Enabled);
    }

    // Veli portalı kapalı okulda veli girişi ve "Veli girişleri" ekranı yoktur; veli kaydının
    // kendisi (iletişim, WhatsApp bildirimi) çekirdekte kalır.
    [Fact]
    public async Task Disabled_parent_portal_closes_guardian_login_but_keeps_guardian_records()
    {
        using var schoolWithoutPortal = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EnabledModules.ConfigurationKey] = ModuleCatalog.NoneSentinel,
            })));

        using var anonymous = schoolWithoutPortal.CreateClient();
        var otp = await anonymous.PostAsJsonAsync("/api/guardian/otp/request", new { phoneNumber = "05551234567" });
        Assert.Equal(HttpStatusCode.NotFound, otp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/guardian/me")).StatusCode);

        var admin = await LoginAsync(schoolWithoutPortal, "admin@test.local", "Test1234!");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/guardians/logins")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/guardians")).StatusCode);
    }

    // Grup dersleri ve gelir-gider kapalı okul: uçlar 404, yeni grup kaydı açılamaz. Kayıt
    // kontrolü öğrenci aranmadan önce yapılır, bu yüzden olmayan bir öğrenciyle de 400 döner.
    [Fact]
    public async Task Disabled_groups_and_finance_close_their_endpoints_and_group_enrollment()
    {
        using var coreOnly = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EnabledModules.ConfigurationKey] = ModuleCatalog.NoneSentinel,
            })));
        var admin = await LoginAsync(coreOnly, "admin@test.local", "Test1234!");

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/lesson-groups")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/expenses")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/recurring-expenses")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/teacher-payouts/week")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/teacher-payouts/group-month?period=2026-10")).StatusCode);

        var unknownStudent = Guid.NewGuid();
        var groupEnrollment = new { teacherId = Guid.NewGuid(), instrumentId = Guid.NewGuid(), startedAt = "2026-10-01", courseKind = "Group" };
        var refused = await admin.PostAsJsonAsync($"/api/students/{unknownStudent}/enrollments", groupEnrollment);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Grup dersleri bu okulda açık değil", await refused.Content.ReadAsStringAsync());

        // Aynı istek tüm modüller açıkken grup kontrolünü geçer ve öğrenci aramasına düşer.
        var everything = await LoginAsync(_factory, "admin@test.local", "Test1234!");
        var passed = await everything.PostAsJsonAsync($"/api/students/{unknownStudent}/enrollments", groupEnrollment);
        Assert.Equal(HttpStatusCode.NotFound, passed.StatusCode);
    }

    // WhatsApp, e-posta ve AI kapalı okul: sağlayıcı yapılandırılmış olsa bile veliye iş kurulmaz,
    // Mesaj Merkezi/webhook/OTP uçları kapanır, AI üreticisi "kullanılamaz" görünür.
    [Fact]
    public async Task Disabled_whatsapp_email_and_ai_do_not_schedule_or_expose_anything()
    {
        using var coreOnly = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EnabledModules.ConfigurationKey] = ModuleCatalog.NoneSentinel,
            })));

        await using (var db = await _factory.CreateDbContextAsync())
        {
            var guardian = Abdera.Api.Modules.People.Domain.Guardian.Create("Rıza", "Açık", "0555 777 66 55", DateTimeOffset.UtcNow);
            guardian.SetNotificationConsent(true, DateTimeOffset.UtcNow);
            db.Guardians.Add(guardian);
            await db.SaveChangesAsync();

            using var offScope = coreOnly.Services.CreateScope();
            var offScheduler = offScope.ServiceProvider.GetRequiredService<Abdera.Api.Modules.Messaging.Features.INotificationScheduler>();
            Assert.False(await offScheduler.ScheduleAsync(
                Abdera.Api.Modules.Messaging.Domain.NotificationJobType.Birthday, "test", Guid.NewGuid(), guardian.Id, DateTimeOffset.UtcNow.AddDays(1)));
            Assert.False(offScope.ServiceProvider.GetRequiredService<Abdera.Api.Modules.Progress.Domain.IProgressSummaryGenerator>().IsAvailable);

            // Aynı veliye modül açıkken iş kurulur - false yalnızca modülden geliyor.
            using var onScope = _factory.Services.CreateScope();
            var onScheduler = onScope.ServiceProvider.GetRequiredService<Abdera.Api.Modules.Messaging.Features.INotificationScheduler>();
            Assert.True(await onScheduler.ScheduleAsync(
                Abdera.Api.Modules.Messaging.Domain.NotificationJobType.Birthday, "test", Guid.NewGuid(), guardian.Id, DateTimeOffset.UtcNow.AddDays(1)));
        }

        using var anonymous = coreOnly.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/webhooks/whatsapp")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/api/guardian/otp/request", new { phoneNumber = "05557776655" })).StatusCode);

        var admin = await LoginAsync(coreOnly, "admin@test.local", "Test1234!");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/notification-automation-settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/me/notifications")).StatusCode);
    }

    // Okul kuralı yöneticiyle değişir, audit'e yazılır ve hesabın tek yerine (grup dersi payı) yansır.
    [Fact]
    public async Task Admin_changes_school_policy_and_group_share_follows_it()
    {
        var admin = await LoginAsync(_factory, "admin@test.local", "Test1234!");
        var teacher = await CreateTeacherClientAsync(admin);

        var current = await admin.GetFromJsonAsync<SchoolPolicyEndpoints.PolicyResponse>("/api/instance/policy", TestJson.Options);
        Assert.NotNull(current);

        var update = new SchoolPolicyEndpoints.UpdateRequest(35m, 3, 12, 30, 14, new TimeOnly(10, 0), new TimeOnly(20, 0));
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync("/api/instance/policy", update)).StatusCode);

        try
        {
            var saved = await admin.PutAsJsonAsync("/api/instance/policy", update);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

            var month = await admin.GetFromJsonAsync<JsonElement>("/api/teacher-payouts/group-month", TestJson.Options);
            Assert.Equal(35m, month.GetProperty("sharePercent").GetDecimal());

            var config = await admin.GetFromJsonAsync<InstanceConfig.ConfigResponse>("/api/instance/config", TestJson.Options);
            Assert.Equal(new TimeOnly(10, 0), config!.SchoolDay.Start);

            await using var db = await _factory.CreateDbContextAsync();
            Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "school_policy.updated"));
        }
        finally
        {
            // Paylaşılan test veritabanında diğer testler varsayılan kurallarla çalışsın.
            await admin.PutAsJsonAsync("/api/instance/policy", new SchoolPolicyEndpoints.UpdateRequest(
                current!.GroupLessonSharePercent, current.MaxWeeklyLessons, current.MakeupNoticeHours,
                current.MakeupCreditValidDays, current.ExcusedMakeupWindowDays, current.SchoolDayStart, current.SchoolDayEnd));
        }
    }

    private static MultipartFormDataContent LogoForm(byte[] content, string claimedType)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(claimedType);
        return new MultipartFormDataContent { { file, "file", "logo.png" } };
    }

    private static async Task<HttpClient> LoginAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string email, string password)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request(email, password));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> CreateTeacherClientAsync(HttpClient admin)
    {
        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        var piano = instruments!.Single(instrument => instrument.Code == "PIANO");
        var email = $"instance.{Guid.NewGuid():N}@test.local";
        var created = await admin.PostAsJsonAsync("/api/teachers", new Teachers.CreateRequest("Kurulum", "Ogretmen", [piano.Id], email));
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options);
        return await LoginAsync(_factory, email, body!.TemporaryPassword!);
    }
}
