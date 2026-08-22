using Abdera.Api.Modules.Ops.Domain;

namespace Abdera.Api.Modules.Ops.Infrastructure;

// Email__Provider=Fake (dev/test varsayılanı) - gerçek bir SMTP sunucusuna bağlanmaz, loglar.
public class FakeEmailSender(ILogger<FakeEmailSender> logger) : IEmailSender
{
    public Task SendAsync(IReadOnlyList<string> toAddresses, string subject, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[FakeEmailSender] -> {To} | Konu: {Subject}\n{Body}",
            string.Join(", ", toAddresses), subject, body);
        return Task.CompletedTask;
    }
}
