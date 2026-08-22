namespace Abdera.Api.Modules.Ops.Domain;

// Aynı Fake/gerçek ikilisi (bkz. IBackupStorage) - FakeEmailSender (dev/test varsayılanı,
// loglar) + SmtpEmailSender (gerçek, Email__SmtpHost/Port/Username/Password ile herhangi
// bir SMTP sağlayıcısı - Gmail uygulama şifresi en kolay kurulum, ama kod sağlayıcıya
// bağımlı değil).
public interface IEmailSender
{
    Task SendAsync(IReadOnlyList<string> toAddresses, string subject, string body, CancellationToken cancellationToken = default);
}
