using System.Net;
using System.Net.Mail;
using Abdera.Api.Modules.Ops.Domain;
using Microsoft.Extensions.Options;

namespace Abdera.Api.Modules.Ops.Infrastructure;

public class SmtpEmailSenderOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public bool UseSsl { get; set; } = true;
}

// Email__Provider=Smtp - herhangi bir SMTP sağlayıcısıyla çalışır (kod sağlayıcıya bağımlı
// değil, CLAUDE.md). En kolay kurulum: Gmail - hesapta 2 Adımlı Doğrulama açılıp
// myaccount.google.com/apppasswords'ten bir "Uygulama Şifresi" üretilir, o şifre
// Email__SmtpPassword olarak girilir (Host=smtp.gmail.com, Port=587, UseSsl=true).
// .NET'in yerleşik System.Net.Mail.SmtpClient'ı kullanıldı - tek, düşük hacimli alarm
// e-postası için MailKit gibi ek bir bağımlılık eklemeye gerek görülmedi (docs/10-decisions.md G).
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpEmailSenderOptions _options;

    public SmtpEmailSender(IOptions<SmtpEmailSenderOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(IReadOnlyList<string> toAddresses, string subject, string body, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            Credentials = new NetworkCredential(_options.Username, _options.Password),
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress),
            Subject = subject,
            Body = body,
        };
        foreach (var address in toAddresses)
        {
            message.To.Add(address);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
