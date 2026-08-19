using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// docs/09-testing.md item 1: "migration'lar boş bir veritabanında baştan sona çalışıyor".
public class MigrationTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public MigrationTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Migrations_apply_cleanly_on_empty_database()
    {
        await using var db = await _factory.CreateDbContextAsync();

        var pending = await db.Database.GetPendingMigrationsAsync();

        Assert.Empty(pending);
    }

    [Fact]
    public async Task Migrations_are_idempotent_when_applied_twice()
    {
        await using var db = await _factory.CreateDbContextAsync();

        // MigrateAsync ikinci kez çağrılınca hata vermemeli - __EFMigrationsHistory
        // tablosu zaten uygulanmış migration'ları atlar.
        await db.Database.MigrateAsync();

        var pending = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Users_table_enforces_unique_email()
    {
        await using var db = await _factory.CreateDbContextAsync();

        var now = DateTimeOffset.UtcNow;
        var user1 = Abdera.Api.Modules.Auth.Domain.User.Create("duplicate@abdera.test", "hash1", Abdera.Api.Modules.Auth.Domain.UserRole.Admin, now);
        var user2 = Abdera.Api.Modules.Auth.Domain.User.Create("duplicate@abdera.test", "hash2", Abdera.Api.Modules.Auth.Domain.UserRole.Teacher, now);

        db.Users.Add(user1);
        await db.SaveChangesAsync();

        db.Users.Add(user2);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
