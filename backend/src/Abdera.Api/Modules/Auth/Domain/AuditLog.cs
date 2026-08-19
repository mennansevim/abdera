namespace Abdera.Api.Modules.Auth.Domain;

// docs/03-erd.md - Auth > audit_log
// CLAUDE.md: para, takvim ve rıza değiştiren her use-case burayı yazar.
// Kayıtlar asla silinmez veya güncellenmez - yalnızca eklenir.
public class AuditLog
{
    public Guid Id { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AuditLog() { }

    public static AuditLog Record(
        Guid? actorUserId,
        string action,
        string entityType,
        Guid entityId,
        DateTimeOffset now,
        string? beforeJson = null,
        string? afterJson = null)
    {
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            CreatedAt = now,
        };
    }
}
