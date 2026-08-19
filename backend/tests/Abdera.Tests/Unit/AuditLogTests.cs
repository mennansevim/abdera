using Abdera.Api.Modules.Auth.Domain;

namespace Abdera.Tests.Unit;

public class AuditLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_allows_null_actor_for_system_triggered_events()
    {
        // Örnek: AdminBootstrapper ilk yöneticiyi oluştururken kimlik doğrulanmış bir
        // kullanıcı yoktur - actorUserId null olabilmeli.
        var entry = AuditLog.Record(null, "user.bootstrap_admin_created", "User", Guid.NewGuid(), Now);

        Assert.Null(entry.ActorUserId);
        Assert.Equal("user.bootstrap_admin_created", entry.Action);
        Assert.Equal(Now, entry.CreatedAt);
    }

    [Fact]
    public void Record_captures_actor_and_entity_reference()
    {
        var actorId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var entry = AuditLog.Record(actorId, "user.password_reset_by_admin", "User", entityId, Now);

        Assert.Equal(actorId, entry.ActorUserId);
        Assert.Equal("User", entry.EntityType);
        Assert.Equal(entityId, entry.EntityId);
    }
}
