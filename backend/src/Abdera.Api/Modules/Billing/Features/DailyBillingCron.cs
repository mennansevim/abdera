using Abdera.Api.Modules.Billing.Infrastructure;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Billing.Features;

// Vercel Cron'un günlük çağırdığı uç (vercel.json "crons"). Serverless yayında
// (Runtime:Serverless=true) BackgroundService'ler çalışmadığı için aylık aidat üretimi ve vadesi
// geçen aidat taraması bu uçtan tetiklenir. Kalıcı container kurulumunda (docker compose)
// arka plan servisleri aynı işi zaten yapar; bu uç orada gereksizdir ama zararsızdır (idempotent).
//
// Kimlik doğrulama: CronAuth (CRON_SECRET yoksa 404, yanlış sırla 401).
public static class DailyBillingCron
{
    public static void MapDailyBillingCron(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/internal/cron/billing-daily", RunAsync).AllowAnonymous();
    }

    private static async Task<IResult> RunAsync(
        HttpContext context, IConfiguration configuration, AbderaDbContext db, IClock clock, ILoggerFactory loggerFactory)
    {
        if (CronAuth.Reject(context, configuration) is { } rejection) return rejection;

        var result = await BillingDailyJob.RunAsync(db, clock, context.RequestAborted);
        loggerFactory.CreateLogger("Abdera.Billing.DailyCron").LogInformation(
            "Günlük aidat işi: {Period} için {Created} aidat açıldı, {Missing} kayıt tarifesiz, {Overdue} aidat gecikti.",
            result.Period, result.CreatedCount, result.MissingTariffCount, result.MarkedOverdueCount);
        return Results.Ok(result);
    }
}
