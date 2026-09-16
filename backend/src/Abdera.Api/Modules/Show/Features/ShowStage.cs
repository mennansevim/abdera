using Abdera.Api.Modules.Show.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Show.Features;

// Gösteri gecesinin canlı akışı. Run-of-show araçlarının (StageManager.tech, Rundown
// Studio) ortak deseni birebir alındı:
//
//   * Tek bir işaretçi, tüm ekranlarda senkron - sahne ekranı, kulis tableti ve yönetici
//     aynı anı görür. İşaretçi veritabanında (ShowEvent.CurrentItemId).
//   * ŞU AN / SIRADAKİ / ONDAN SONRAKİ üçlüsü ("now / next / on deck") - kulisteki
//     görevlinin bir sonraki öğrenciyi sahne kenarına çağırabilmesi bu üçüncü satıra bağlı.
//   * Tek tuşla ilerletme (ileri/geri), ve yanlış basınca geri alınabilirlik.
//   * Geçen süre ile planlanan sürenin karşılaştırılması - "programın kaç dakika
//     gerisindeyiz" sorusu gösteri gecesinin en sık sorulan sorusu.
public static class ShowStage
{
    public record StageItemResponse(
        Guid Id, int Position, string? GroupName, ShowItemKind Kind,
        Guid? StudentId, string? StudentName, bool HasPhoto, string? PhotoVersion,
        string? InstrumentName, string? TeacherName,
        string? PieceTitle, string? Composer, int? DurationMinutes, string? Note,
        // Bu öğrencinin programdaki tüm eserleri ve hangisinin çalındığı. Sahne ekranı
        // "Bu öğrenci 2 eser çalıyor, şu an 1.si" diyebilsin.
        List<string> StudentPieces, int StudentPieceIndex);

    public record StageResponse(
        Guid ShowId, string Title, string? VenueName, ShowEventStatus Status,
        DateTimeOffset? StartedAt, DateTimeOffset StartsAt,
        StageItemResponse? Current, StageItemResponse? Next, StageItemResponse? OnDeck,
        int TotalItems, int CompletedItems, int TotalDurationMinutes, int ElapsedMinutes);

    public static void MapShowStage(this IEndpointRouteBuilder app)
    {
        // Kulisteki öğretmen de bakabilmeli - sahne ekranı operasyonel bir belge.
        app.MapGet("/api/shows/{showId:guid}/stage", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        var control = app.MapGroup("/api/shows/{showId:guid}").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        control.MapPost("/start", StartAsync);
        control.MapPost("/advance", AdvanceAsync);
        control.MapPost("/back", BackAsync);
        control.MapPost("/goto/{itemId:guid}", GoToAsync);
        control.MapPost("/finish", FinishAsync);
        control.MapPost("/reopen", ReopenAsync);
    }

    private static async Task<IResult> GetAsync(Guid showId, AbderaDbContext db, IClock clock) =>
        Results.Ok(await BuildAsync(showId, db, clock));

    private static async Task<IResult> StartAsync(Guid showId, AbderaDbContext db, IClock clock)
    {
        var show = await LoadAsync(showId, db);
        var now = clock.UtcNow;
        show.Start(now);

        // Başlatınca ilk sıraya geçilir - "başlat" deyip sonra ayrıca "ilerlet" demek
        // gösteri gecesinde fazladan bir adım.
        var first = await db.ShowItems
            .Where(item => item.ShowEventId == showId)
            .OrderBy(item => item.Position)
            .FirstOrDefaultAsync();
        show.MoveTo(first?.Id, now);

        await db.SaveChangesAsync();
        return Results.Ok(await BuildAsync(showId, db, clock));
    }

    private static Task<IResult> AdvanceAsync(Guid showId, AbderaDbContext db, IClock clock) =>
        StepAsync(showId, db, clock, forward: true);

    private static Task<IResult> BackAsync(Guid showId, AbderaDbContext db, IClock clock) =>
        StepAsync(showId, db, clock, forward: false);

    private static async Task<IResult> StepAsync(Guid showId, AbderaDbContext db, IClock clock, bool forward)
    {
        var show = await LoadAsync(showId, db);
        if (show.Status != ShowEventStatus.Live)
            throw new ConflictException("Gösteri başlatılmadan sıra ilerletilemez.");

        var items = await db.ShowItems.AsNoTracking()
            .Where(item => item.ShowEventId == showId)
            .OrderBy(item => item.Position)
            .Select(item => item.Id)
            .ToListAsync();

        if (items.Count == 0) throw new ConflictException("Programda sıra yok.");

        var index = show.CurrentItemId is { } currentId ? items.IndexOf(currentId) : -1;
        var nextIndex = forward ? index + 1 : index - 1;

        if (nextIndex >= items.Count)
            throw new ConflictException("Program bitti. Gösteriyi tamamlayabilirsiniz.");
        if (nextIndex < 0)
            throw new ConflictException("Zaten ilk sıradasınız.");

        show.MoveTo(items[nextIndex], clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(await BuildAsync(showId, db, clock));
    }

    private static async Task<IResult> GoToAsync(Guid showId, Guid itemId, AbderaDbContext db, IClock clock)
    {
        var show = await LoadAsync(showId, db);
        if (!await db.ShowItems.AnyAsync(item => item.Id == itemId && item.ShowEventId == showId))
            throw new NotFoundException("Program sırası bulunamadı.");

        show.MoveTo(itemId, clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(await BuildAsync(showId, db, clock));
    }

    private static async Task<IResult> FinishAsync(Guid showId, AbderaDbContext db, IClock clock)
    {
        var show = await LoadAsync(showId, db);
        show.Finish(clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(await BuildAsync(showId, db, clock));
    }

    private static async Task<IResult> ReopenAsync(Guid showId, AbderaDbContext db, IClock clock)
    {
        var show = await LoadAsync(showId, db);
        show.ReopenAsDraft(clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(await BuildAsync(showId, db, clock));
    }

    private static async Task<ShowEvent> LoadAsync(Guid showId, AbderaDbContext db) =>
        await db.ShowEvents.SingleOrDefaultAsync(item => item.Id == showId)
        ?? throw new NotFoundException("Gösteri bulunamadı.");

    private static async Task<StageResponse> BuildAsync(Guid showId, AbderaDbContext db, IClock clock)
    {
        var show = await db.ShowEvents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == showId)
            ?? throw new NotFoundException("Gösteri bulunamadı.");

        var items = await Shows.LoadItemsAsync(showId, db);
        var index = show.CurrentItemId is { } currentId
            ? items.FindIndex(item => item.Id == currentId)
            : -1;

        StageItemResponse? At(int position) =>
            position >= 0 && position < items.Count ? ToStageItem(items[position], items) : null;

        var elapsed = show.StartedAt is { } startedAt
            ? (int)Math.Max(0, (clock.UtcNow - startedAt).TotalMinutes)
            : 0;

        return new StageResponse(
            show.Id, show.Title, show.VenueName, show.Status, show.StartedAt, show.StartsAt,
            At(index), At(index + 1), At(index + 2),
            items.Count,
            // "Kaç sıra geride kaldı" - ilerleme çubuğunun kaynağı.
            index < 0 ? 0 : index,
            items.Sum(item => item.DurationMinutes ?? 0),
            elapsed);
    }

    private static StageItemResponse ToStageItem(Shows.ItemResponse item, List<Shows.ItemResponse> all)
    {
        // Öğrencinin programdaki TÜM eserleri - sahne ekranı "2 eserden 1.si" diyebilsin.
        var pieces = item.StudentId is { } studentId
            ? all.Where(other => other.StudentId == studentId && other.PieceTitle is not null)
                .Select(other => other.PieceTitle!)
                .ToList()
            : [];
        var pieceIndex = item.StudentId is null
            ? 0
            : all.Where(other => other.StudentId == item.StudentId && other.PieceTitle is not null)
                .ToList()
                .FindIndex(other => other.Id == item.Id);

        return new StageItemResponse(
            item.Id, item.Position, item.GroupName, item.Kind,
            item.StudentId, item.StudentName, item.HasPhoto, item.PhotoVersion,
            item.InstrumentName, item.TeacherName,
            item.PieceTitle, item.Composer, item.DurationMinutes, item.Note,
            pieces, Math.Max(0, pieceIndex));
    }
}
