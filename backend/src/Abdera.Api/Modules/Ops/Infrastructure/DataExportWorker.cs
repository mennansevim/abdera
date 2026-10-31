using System.Threading.Channels;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Ops.Infrastructure;

// İstek uç noktası satırı açıp kimliği kuyruğa bırakır; dosyayı bu worker üretir (AJ2). Kuyruk
// bellekte: uygulama yeniden başlarsa açılışta bekleyen ve yarıda kalan satırlar yeniden
// kuyruğa alınır, iş kaybolmaz. Tek okuyucu - aynı anda tek döküm üretilir.
public class DataExportQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid exportId) => _channel.Writer.TryWrite(exportId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

public class DataExportWorker(IServiceScopeFactory scopeFactory, DataExportQueue queue, ILogger<DataExportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedAsync(stoppingToken);
        await foreach (var exportId in queue.ReadAllAsync(stoppingToken))
            await RunAsync(exportId, stoppingToken);
    }

    private async Task RequeueUnfinishedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
            var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
            var unfinished = await db.DataExports
                .Where(e => e.Status == DataExportStatus.Pending || e.Status == DataExportStatus.Running)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(cancellationToken);
            foreach (var export in unfinished) export.Requeue(now);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var export in unfinished) queue.Enqueue(export.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Bekleyen veri dökümleri yeniden kuyruğa alınamadı.");
        }
    }

    private async Task RunAsync(Guid exportId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var export = await db.DataExports.SingleOrDefaultAsync(e => e.Id == exportId, cancellationToken);
        if (export is null || !export.IsInProgress) return;

        export.MarkRunning(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var content = await DataExportWorkbook.BuildAsync(db, clock, cancellationToken);
            var local = clock.ToSchoolLocal(clock.UtcNow);
            export.Complete($"abdera-veri-{local:yyyy-MM-dd-HHmm}.xlsx", content, clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Veri dökümü {ExportId} hazır ({Size} bayt).", export.Id, content.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Veri dökümü {ExportId} üretilemedi.", exportId);
            db.ChangeTracker.Clear();
            var failed = await db.DataExports.SingleAsync(e => e.Id == exportId, cancellationToken);
            failed.Fail($"{ex.GetType().Name}: {ex.Message}", clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }

        await PruneAsync(db, cancellationToken);
    }

    // Son KeepLatest bitmiş döküm kalır; sürmekte olan satır sayılmaz ve silinmez.
    private static async Task PruneAsync(AbderaDbContext db, CancellationToken cancellationToken)
    {
        var keep = await db.DataExports
            .Where(e => e.Status == DataExportStatus.Completed || e.Status == DataExportStatus.Failed)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.Id)
            .Take(DataExport.KeepLatest)
            .ToListAsync(cancellationToken);
        await db.DataExports
            .Where(e => (e.Status == DataExportStatus.Completed || e.Status == DataExportStatus.Failed) && !keep.Contains(e.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
