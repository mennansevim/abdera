using Abdera.Api.Modules.Ops.Domain;

namespace Abdera.Api.Modules.Ops.Infrastructure;

// Email__Provider=Disabled: okulun e-posta hesabı yok (docs/multi-tenant/ARCHITECTURE.md §5.2,
// "email" modülü). Production'da geçerli bir seçim; alarmlar platforma Alerts__WebhookUrl ile gider.
public class DisabledEmailSender(ILogger<DisabledEmailSender> logger) : IEmailSender
{
    public Task SendAsync(IReadOnlyList<string> toAddresses, string subject, string body, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("E-posta devre dışı; gönderilmedi: {Subject}", subject);
        return Task.CompletedTask;
    }
}
