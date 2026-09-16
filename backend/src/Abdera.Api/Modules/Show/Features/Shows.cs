using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Show.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Show.Features;

// Yıl sonu gösterisinin programı: sıralı bir liste ve onu düzenleyen uçlar.
//
// Öğretmen programı GÖREBİLİR (kendi öğrencisi kaçıncı sırada, ne çalıyor - kulis
// düzeninin tamamı buna bağlı), yalnızca yönetici DEĞİŞTİREBİLİR. Bu, aidattan farklı:
// aidat tamamen Admin'e kapalı (docs/04-permissions.md), program ise operasyonel bir belge.
public static class Shows
{
    public record CreateRequest(string Title, string? VenueName, DateTimeOffset StartsAt);
    public record ItemRequest(
        ShowItemKind Kind, string? GroupName, Guid? StudentId, Guid? InstrumentId, Guid? TeacherId,
        string? PieceTitle, string? Composer, int? DurationMinutes, string? Note);
    public record ReorderRequest(List<Guid> ItemIds);

    public record ShowSummaryResponse(
        Guid Id, string Title, string? VenueName, DateTimeOffset StartsAt, ShowEventStatus Status,
        int ItemCount, int PerformerCount, int TotalDurationMinutes);

    public record ItemResponse(
        Guid Id, int Position, string? GroupName, ShowItemKind Kind,
        Guid? StudentId, string? StudentName, bool HasPhoto, string? PhotoVersion,
        Guid? InstrumentId, string? InstrumentName, Guid? TeacherId, string? TeacherName,
        string? PieceTitle, string? Composer, int? DurationMinutes, string? Note);

    public record ShowDetailResponse(
        Guid Id, string Title, string? VenueName, DateTimeOffset StartsAt, ShowEventStatus Status,
        Guid? CurrentItemId, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt,
        int TotalDurationMinutes, List<ItemResponse> Items);

    public static void MapShows(this IEndpointRouteBuilder app)
    {
        var read = app.MapGroup("/api/shows").RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        read.MapGet("", ListAsync);
        read.MapGet("/{showId:guid}", GetAsync);

        var write = app.MapGroup("/api/shows").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        write.MapPost("", CreateAsync);
        write.MapPatch("/{showId:guid}", UpdateAsync);
        write.MapDelete("/{showId:guid}", DeleteAsync);
        write.MapPost("/{showId:guid}/items", AddItemAsync);
        write.MapPatch("/{showId:guid}/items/{itemId:guid}", UpdateItemAsync);
        write.MapDelete("/{showId:guid}/items/{itemId:guid}", DeleteItemAsync);
        write.MapPost("/{showId:guid}/items/reorder", ReorderAsync);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var shows = await db.ShowEvents.AsNoTracking().OrderByDescending(show => show.StartsAt).ToListAsync();
        var showIds = shows.Select(show => show.Id).ToList();
        var items = await db.ShowItems.AsNoTracking()
            .Where(item => showIds.Contains(item.ShowEventId))
            .Select(item => new { item.ShowEventId, item.Kind, item.StudentId, item.DurationMinutes })
            .ToListAsync();

        return Results.Ok(shows.Select(show =>
        {
            var own = items.Where(item => item.ShowEventId == show.Id).ToList();
            return new ShowSummaryResponse(
                show.Id, show.Title, show.VenueName, show.StartsAt, show.Status,
                own.Count,
                own.Where(item => item.StudentId.HasValue).Select(item => item.StudentId!.Value).Distinct().Count(),
                own.Sum(item => item.DurationMinutes ?? 0));
        }).ToList());
    }

    private static async Task<IResult> GetAsync(Guid showId, AbderaDbContext db)
    {
        var show = await db.ShowEvents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == showId)
            ?? throw new NotFoundException("Gösteri bulunamadı.");

        var items = await LoadItemsAsync(showId, db);
        return Results.Ok(new ShowDetailResponse(
            show.Id, show.Title, show.VenueName, show.StartsAt, show.Status,
            show.CurrentItemId, show.StartedAt, show.EndedAt,
            items.Sum(item => item.DurationMinutes ?? 0), items));
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var now = clock.UtcNow;
        var show = ShowEvent.Create(request.Title, request.VenueName, request.StartsAt, now);
        db.ShowEvents.Add(show);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "show.created", nameof(ShowEvent), show.Id, now,
            afterJson: JsonSerializer.Serialize(new { show.Title, show.VenueName, show.StartsAt })));
        await db.SaveChangesAsync();

        return Results.Created($"/api/shows/{show.Id}", new ShowDetailResponse(
            show.Id, show.Title, show.VenueName, show.StartsAt, show.Status,
            null, null, null, 0, []));
    }

    private static async Task<IResult> UpdateAsync(
        Guid showId, CreateRequest request, AbderaDbContext db, IClock clock)
    {
        var show = await db.ShowEvents.SingleOrDefaultAsync(item => item.Id == showId)
            ?? throw new NotFoundException("Gösteri bulunamadı.");

        show.Update(request.Title, request.VenueName, request.StartsAt, clock.UtcNow);
        await db.SaveChangesAsync();
        return await GetAsync(showId, db);
    }

    private static async Task<IResult> DeleteAsync(
        Guid showId, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var show = await db.ShowEvents.SingleOrDefaultAsync(item => item.Id == showId)
            ?? throw new NotFoundException("Gösteri bulunamadı.");

        var now = clock.UtcNow;
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "show.deleted", nameof(ShowEvent), show.Id, now,
            beforeJson: JsonSerializer.Serialize(new { show.Title, show.StartsAt, Status = show.Status.ToString() })));
        db.ShowEvents.Remove(show);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> AddItemAsync(
        Guid showId, ItemRequest request, AbderaDbContext db, IClock clock)
    {
        if (!await db.ShowEvents.AnyAsync(item => item.Id == showId))
            throw new NotFoundException("Gösteri bulunamadı.");
        await ValidateReferencesAsync(request, db);

        var lastPosition = await db.ShowItems
            .Where(item => item.ShowEventId == showId)
            .MaxAsync(item => (int?)item.Position) ?? -1;

        var item = ShowItem.Create(
            showId, lastPosition + 1, request.Kind, request.GroupName,
            request.StudentId, request.InstrumentId, request.TeacherId,
            request.PieceTitle, request.Composer, request.DurationMinutes, request.Note, clock.UtcNow);

        db.ShowItems.Add(item);
        await db.SaveChangesAsync();
        return await GetAsync(showId, db);
    }

    private static async Task<IResult> UpdateItemAsync(
        Guid showId, Guid itemId, ItemRequest request, AbderaDbContext db, IClock clock)
    {
        var item = await db.ShowItems.SingleOrDefaultAsync(row => row.Id == itemId && row.ShowEventId == showId)
            ?? throw new NotFoundException("Program sırası bulunamadı.");
        await ValidateReferencesAsync(request, db);

        item.Apply(
            request.Kind, request.GroupName, request.StudentId, request.InstrumentId, request.TeacherId,
            request.PieceTitle, request.Composer, request.DurationMinutes, request.Note, clock.UtcNow);

        await db.SaveChangesAsync();
        return await GetAsync(showId, db);
    }

    private static async Task<IResult> DeleteItemAsync(Guid showId, Guid itemId, AbderaDbContext db, IClock clock)
    {
        var item = await db.ShowItems.SingleOrDefaultAsync(row => row.Id == itemId && row.ShowEventId == showId)
            ?? throw new NotFoundException("Program sırası bulunamadı.");

        var show = await db.ShowEvents.SingleAsync(row => row.Id == showId);
        // Silinen sıra sahnedeyse işaretçi boşa alınır - aksi halde canlı ekran var
        // olmayan bir satırı göstermeye çalışırdı.
        if (show.CurrentItemId == itemId && show.Status == ShowEventStatus.Live)
            show.MoveTo(null, clock.UtcNow);

        db.ShowItems.Remove(item);
        await db.SaveChangesAsync();

        await RenumberAsync(showId, db, clock.UtcNow);
        return await GetAsync(showId, db);
    }

    // Sürükle-bırak sonrası tüm sıra tek çağrıda yazılır. Tek tek "yukarı/aşağı" uçları
    // yerine bütün listeyi almak, yarım kalmış bir sıralamayı imkânsız kılar.
    private static async Task<IResult> ReorderAsync(
        Guid showId, ReorderRequest request, AbderaDbContext db, IClock clock)
    {
        var items = await db.ShowItems.Where(item => item.ShowEventId == showId).ToListAsync();
        var byId = items.ToDictionary(item => item.Id);

        if (request.ItemIds.Count != items.Count || request.ItemIds.Any(id => !byId.ContainsKey(id)))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["itemIds"] = ["Sıralama listesi programdaki satırların tamamını birebir içermeli."],
            });

        var now = clock.UtcNow;
        for (var index = 0; index < request.ItemIds.Count; index++)
        {
            byId[request.ItemIds[index]].MoveTo(index, now);
        }

        await db.SaveChangesAsync();
        return await GetAsync(showId, db);
    }

    private static async Task RenumberAsync(Guid showId, AbderaDbContext db, DateTimeOffset now)
    {
        var items = await db.ShowItems
            .Where(item => item.ShowEventId == showId)
            .OrderBy(item => item.Position)
            .ToListAsync();

        var changed = false;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Position == index) continue;
            items[index].MoveTo(index, now);
            changed = true;
        }

        if (changed) await db.SaveChangesAsync();
    }

    private static async Task ValidateReferencesAsync(ItemRequest request, AbderaDbContext db)
    {
        if (request.StudentId is { } studentId && !await db.Students.AnyAsync(s => s.Id == studentId))
            throw new NotFoundException("Öğrenci bulunamadı.");
        if (request.InstrumentId is { } instrumentId && !await db.Instruments.AnyAsync(i => i.Id == instrumentId))
            throw new NotFoundException("Enstrüman bulunamadı.");
        if (request.TeacherId is { } teacherId && !await db.Teachers.AnyAsync(t => t.Id == teacherId))
            throw new NotFoundException("Öğretmen bulunamadı.");
    }

    // CLAUDE.md: birden fazla Join sonrası record'a projekte eden sorguda OrderBy'ı
    // projeksiyondan SONRA koyma. Burada join yerine açık id sorguları kullanılıyor
    // (modül sınırı kuralı) ve sıralama bellekte, ham entity üzerinde yapılıyor.
    internal static async Task<List<ItemResponse>> LoadItemsAsync(Guid showId, AbderaDbContext db)
    {
        var items = await db.ShowItems.AsNoTracking()
            .Where(item => item.ShowEventId == showId)
            .OrderBy(item => item.Position)
            .ToListAsync();

        var studentIds = items.Where(item => item.StudentId.HasValue).Select(item => item.StudentId!.Value).Distinct().ToList();
        var teacherIds = items.Where(item => item.TeacherId.HasValue).Select(item => item.TeacherId!.Value).Distinct().ToList();
        var instrumentIds = items.Where(item => item.InstrumentId.HasValue).Select(item => item.InstrumentId!.Value).Distinct().ToList();

        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        var instruments = await db.Instruments.AsNoTracking().Where(i => instrumentIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        // Yalnızca sürüm anahtarı çekilir - blob'un kendisi asla program yanıtına girmez.
        var photos = await db.StudentPhotos.AsNoTracking()
            .Where(photo => studentIds.Contains(photo.StudentId))
            .Select(photo => new { photo.StudentId, photo.Version })
            .ToDictionaryAsync(photo => photo.StudentId, photo => photo.Version);

        return items.Select(item =>
        {
            var student = item.StudentId is { } sid && students.TryGetValue(sid, out var s) ? s : null;
            var teacher = item.TeacherId is { } tid && teachers.TryGetValue(tid, out var t) ? t : null;
            var instrument = item.InstrumentId is { } iid && instruments.TryGetValue(iid, out var i) ? i : null;
            var hasPhoto = item.StudentId is { } pid && photos.ContainsKey(pid);

            return new ItemResponse(
                item.Id, item.Position, item.GroupName, item.Kind,
                item.StudentId, student is null ? null : $"{student.FirstName} {student.LastName}",
                hasPhoto, hasPhoto ? photos[item.StudentId!.Value].ToString("N") : null,
                item.InstrumentId, instrument?.Name,
                item.TeacherId, teacher is null ? null : $"{teacher.FirstName} {teacher.LastName}",
                item.PieceTitle, item.Composer, item.DurationMinutes, item.Note);
        }).ToList();
    }
}
