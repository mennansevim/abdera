using System.Security.Cryptography;
using System.Text;

namespace Abdera.Api.Shared;

// Vercel Cron uçlarının ortak kapısı (DailyBillingCron, LessonNoteReminderCron). Vercel,
// projede CRON_SECRET ortam değişkeni tanımlıysa cron isteğine `Authorization: Bearer
// <CRON_SECRET>` ekler. Değişken tanımlı değilse uç kapalıdır (404) - uçlar oturum
// gerektirmediği için sırsız bir kurulumda herkese açık bir tetikleyici olmamalı.
// Değer istek anında okunur (CLAUDE.md: Program.cs'te Build() öncesi eager okuma yok).
public static class CronAuth
{
    /// <returns>İstek reddedilecekse döndürülecek sonuç; yetkiliyse null.</returns>
    public static IResult? Reject(HttpContext context, IConfiguration configuration)
    {
        var secret = configuration["CRON_SECRET"];
        if (string.IsNullOrWhiteSpace(secret)) return Results.NotFound();

        var expected = Encoding.UTF8.GetBytes($"Bearer {secret}");
        var actual = Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString());
        return CryptographicOperations.FixedTimeEquals(actual, expected) ? null : Results.Unauthorized();
    }
}
