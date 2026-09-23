using System.Security.Cryptography;
using System.Text;
using Abdera.Api.Modules.Billing.Infrastructure;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Billing.Features;

// Vercel Cron'un günlük çağırdığı uç (vercel.json "crons"). Serverless yayında
// (Runtime:Serverless=true) BackgroundService'ler çalışmadığı için aylık aidat üretimi ve vadesi
// geçen aidat taraması bu uçtan tetiklenir. Kalıcı container kurulumunda (docker compose)
// arka plan servisleri aynı işi zaten yapar; bu uç orada gereksizdir ama zararsızdır (idempotent).
//
// Kimlik doğrulama: Vercel, projede CRON_SECRET ortam değişkeni tanımlıysa cron isteğine
// `Authorization: Bearer <CRON_SECRET>` ekler. Değişken tanımlı değilse uç kapalıdır (404) - uç
// oturum gerektirmediği için sırsız bir kurulumda herkese açık bir tetikleyici olmamalı.
// Değer istek anında okunur (CLAUDE.md: Program.cs'te Build() öncesi eager okuma yok).
public static class DailyBillingCron
{
    public static void MapDailyBillingCron(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/internal/cron/billing-daily", RunAsync).AllowAnonymous();
    }

    private static async Task<IResult> RunAsync(
        HttpContext context, IConfiguration configuration, AbderaDbContext db, IClock clock, ILoggerFactory loggerFactory)
    {
        var secret = configuration["CRON_SECRET"];
        if (string.IsNullOrWhiteSpace(secret)) return Results.NotFound();
        if (!IsAuthorized(context.Request.Headers.Authorization.ToString(), secret)) return Results.Unauthorized();

        var result = await BillingDailyJob.RunAsync(db, clock, context.RequestAborted);
        loggerFactory.CreateLogger("Abdera.Billing.DailyCron").LogInformation(
            "Günlük aidat işi: {Period} için {Created} aidat açıldı, {Missing} kayıt tarifesiz, {Overdue} aidat gecikti.",
            result.Period, result.CreatedCount, result.MissingTariffCount, result.MarkedOverdueCount);
        return Results.Ok(result);
    }

    private static bool IsAuthorized(string header, string secret)
    {
        var expected = Encoding.UTF8.GetBytes($"Bearer {secret}");
        var actual = Encoding.UTF8.GetBytes(header);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
