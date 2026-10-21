using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Abdera.Tests.Integration;

// docs/multi-tenant/ARCHITECTURE.md §5.7: kullanıcı paketi. Sayılan yalnızca etkin yönetici + öğretmen
// hesabı; paket doluyken yeni hesap açılmaz ve pasif hesap geri açılmaz, hesapsız kayıt etkilenmez.
public class StaffSeatFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public StaffSeatFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Without_a_limit_the_school_is_unlimited()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync(_factory);

        var seats = await admin.GetFromJsonAsync<StaffSeats.Response>("/api/staff-seats", TestJson.Options);
        Assert.Null(seats!.Limit);
        Assert.True(seats.Used >= 1);

        var created = await admin.PostAsJsonAsync("/api/teachers", await TeacherRequestAsync(admin, withEmail: true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task Full_package_blocks_new_staff_accounts_but_not_teachers_without_login()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var used = await StaffSeats.CountUsedAsync(db);
        using var school = WithLimit(used + 1);
        var admin = await LoginAsync(school);

        var seats = await admin.GetFromJsonAsync<StaffSeats.Response>("/api/staff-seats", TestJson.Options);
        Assert.Equal(new StaffSeats.Response(used + 1, used), seats);

        // Son boş koltuk kullanılır, ardından paket dolar.
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/teachers", await TeacherRequestAsync(admin, withEmail: true))).StatusCode);
        var blocked = await admin.PostAsJsonAsync("/api/teachers", await TeacherRequestAsync(admin, withEmail: true));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("paketiniz dolu", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(used + 1, await StaffSeats.CountUsedAsync(db));

        // Giriş hesabı olmayan öğretmen kaydı pakete sayılmaz.
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/teachers", await TeacherRequestAsync(admin, withEmail: false))).StatusCode);

        // Veli hesapları sayılmaz.
        db.Users.Add(Abdera.Api.Modules.Auth.Domain.User.Create(
            $"veli.{Guid.NewGuid():N}@test.local", "placeholder", Abdera.Api.Modules.Auth.Domain.UserRole.Guardian, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        Assert.Equal(used + 1, (await admin.GetFromJsonAsync<StaffSeats.Response>("/api/staff-seats", TestJson.Options))!.Used);
    }

    [Fact]
    public async Task Reactivating_a_teacher_needs_a_free_seat_and_shrinking_the_package_keeps_existing_accounts()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync(_factory);
        var request = await TeacherRequestAsync(admin, withEmail: true);
        var created = await (await admin.PostAsJsonAsync("/api/teachers", request)).Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options);
        var teacherId = created!.Teacher.Id;

        var deactivate = await admin.PatchAsJsonAsync($"/api/teachers/{teacherId}",
            new Teachers.UpdateRequest(request.FirstName, request.LastName, TeacherStatus.Inactive, request.InstrumentIds));
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        // Paket şu an kullanılan sayıya indirilir: var olan hesaplar açık kalır, pasif olan geri açılamaz.
        var used = await StaffSeats.CountUsedAsync(db);
        using var full = WithLimit(used);
        var fullAdmin = await LoginAsync(full);
        var reactivate = await fullAdmin.PatchAsJsonAsync($"/api/teachers/{teacherId}",
            new Teachers.UpdateRequest(request.FirstName, request.LastName, TeacherStatus.Active, request.InstrumentIds));
        Assert.Equal(HttpStatusCode.Conflict, reactivate.StatusCode);
        var teacher = await db.Teachers.AsNoTracking().SingleAsync(t => t.Id == teacherId);
        Assert.Equal(TeacherStatus.Inactive, teacher.Status);
        Assert.False(await db.Users.AsNoTracking().Where(u => u.Id == teacher.UserId).Select(u => u.IsActive).SingleAsync());

        // Aktif öğretmenin adını düzenlemek koltuk istemez.
        var renamed = await fullAdmin.PatchAsJsonAsync($"/api/teachers/{teacherId}",
            new Teachers.UpdateRequest("Yeni", request.LastName, TeacherStatus.Inactive, request.InstrumentIds));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        // Paket büyüyünce geri açılır.
        using var grown = WithLimit(used + 1);
        var grownAdmin = await LoginAsync(grown);
        var reopened = await grownAdmin.PatchAsJsonAsync($"/api/teachers/{teacherId}",
            new Teachers.UpdateRequest(request.FirstName, request.LastName, TeacherStatus.Active, request.InstrumentIds));
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
    }

    private WebApplicationFactory<Program> WithLimit(int limit) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [StaffSeats.ConfigurationKey] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            })));

    private static async Task<Teachers.CreateRequest> TeacherRequestAsync(HttpClient admin, bool withEmail)
    {
        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        var piano = instruments!.Single(instrument => instrument.Code == "PIANO");
        return new Teachers.CreateRequest("Paket", "Ogretmen", [piano.Id], withEmail ? $"seat.{Guid.NewGuid():N}@test.local" : null);
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"));
        response.EnsureSuccessStatusCode();
        return client;
    }
}
