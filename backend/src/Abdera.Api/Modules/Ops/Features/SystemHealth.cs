using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Ops.Features;

// Dashboard ana ekranındaki sağlık kartı için - docs/15-product-phases.md Faz 4.
// Admin-only (docs/04-permissions.md - mali/altyapı verisi Teacher'a görünmez).
public static class SystemHealth
{
    public enum ProviderConfigurationState
    {
        Configured,
        DevelopmentOnly,
        // Banking__Provider=Manual: bilinçli olarak kapatılmış bir entegrasyon. Bu bir hata
        // değil (Misconfigured), ama "her şey hazır" da değil - yönetici sanal IBAN'ın
        // çalışmadığını, ödemeleri elle gireceğini sağlık kartında görebilmeli.
        ManualOnly,
        Misconfigured,
    }

    public record ProviderHealthResponse(
        ProviderConfigurationState WhatsApp,
        ProviderConfigurationState Banking,
        ProviderConfigurationState Backup);

    public record SystemHealthResponse(
        SystemHealthLevel Level, string? Detail, DateTimeOffset LastCheckedAt,
        bool DatabaseReachable,
        DateTimeOffset? LastSuccessfulBackupAt, string? LastBackupStatus,
        ProviderHealthResponse Providers);

    public static void MapSystemHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/system/health", GetAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> GetAsync(AbderaDbContext db, IConfiguration configuration, IHostEnvironment environment)
    {
        var databaseReachable = await db.Database.CanConnectAsync();
        var status = await SystemHealthStatus.GetCurrentAsync(db);
        var latestBackup = await db.BackupRuns.OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync();
        var lastSuccessful = await db.BackupRuns
            .Where(r => r.Status == BackupRunStatus.Succeeded)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync();

        return Results.Ok(new SystemHealthResponse(
            status.Level, status.Detail, status.LastCheckedAt,
            databaseReachable,
            lastSuccessful?.CompletedAt, latestBackup?.Status.ToString(),
            new ProviderHealthResponse(
                ProviderState(configuration["WhatsApp:Provider"], "Cloud", environment),
                ProviderState(configuration["Banking:Provider"], "Fake", environment, fakeIsConfigured: false),
                ProviderState(configuration["Backup:Provider"], "Sftp", environment))));
    }

    private static ProviderConfigurationState ProviderState(
        string? configuredValue,
        string productionValue,
        IHostEnvironment environment,
        bool fakeIsConfigured = true)
    {
        if (environment.IsDevelopment() &&
            (string.IsNullOrWhiteSpace(configuredValue) || string.Equals(configuredValue, "Fake", StringComparison.OrdinalIgnoreCase)))
            return ProviderConfigurationState.DevelopmentOnly;

        // Şu an yalnızca Banking bu değeri alabiliyor (Program.cs) - bilinçli olarak kapalı
        // bir entegrasyon, hata değil.
        if (string.Equals(configuredValue, "Manual", StringComparison.OrdinalIgnoreCase))
            return ProviderConfigurationState.ManualOnly;

        if (fakeIsConfigured && string.Equals(configuredValue, productionValue, StringComparison.OrdinalIgnoreCase))
            return ProviderConfigurationState.Configured;

        if (!fakeIsConfigured && !string.IsNullOrWhiteSpace(configuredValue) &&
            !string.Equals(configuredValue, productionValue, StringComparison.OrdinalIgnoreCase))
            return ProviderConfigurationState.Configured;

        return ProviderConfigurationState.Misconfigured;
    }
}
