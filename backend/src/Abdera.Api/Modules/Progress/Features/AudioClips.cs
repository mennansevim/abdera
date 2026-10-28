using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Progress.Infrastructure;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Features;

// Ses Defteri (issue #52). Öğretmen dersin sonunda kısa bir çalış kaydı alır; kayıt varsayılan
// olarak yalnızca okul ekibine görünür, öğretmen "veliye göster" derse veli portalına düşer.
//
// Kayıt yalnızca öğrencinin ses kaydı rızası açıkken alınabilir. Rıza kapatılınca öğrencinin
// bütün kayıtları (satır + dosya) silinir: KVKK'da rızanın geri alınması işlemenin durması
// demektir. Kayıtlar finansal/audit verisi değildir; silinmeleri CLAUDE.md'ye aykırı değil.
public static class AudioClips
{
    public record ClipResponse(
        Guid Id, Guid StudentId, Guid? LessonId, Guid? TeacherId, string? TeacherName, string? PieceTitle,
        string ContentType, int SizeBytes, int DurationSeconds, bool Starred, bool SharedWithGuardian,
        DateTimeOffset CreatedAt);
    public record StudentClipsResponse(
        Guid StudentId, bool Consent, DateTimeOffset? ConsentUpdatedAt, int TermQuota, int TermUsed,
        List<ClipResponse> Clips);
    public record GuardianClipResponse(
        Guid Id, string? PieceTitle, string? TeacherName, int DurationSeconds, string ContentType, DateTimeOffset CreatedAt);
    public record GuardianClipsResponse(Guid StudentId, bool Consent, List<GuardianClipResponse> Clips);
    public record ConsentRequest(bool Consent);
    public record ConsentResponse(Guid StudentId, bool Consent, int DeletedClips);
    public record ShareRequest(bool Shared);
    public record StarRequest(bool Starred);
    public record UsageResponse(long TotalBytes, int ClipCount, long Last30DaysBytes, int NotBackedUp, long DiskBytes, int DiskFiles);

    private const int DefaultTermQuota = 20;

    public static void MapAudioClips(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lessons/{lessonId:guid}/audio-clips", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin)
            .DisableAntiforgery();
        app.MapGet("/api/students/{studentId:guid}/audio-clips", StaffListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapGet("/api/audio-clips/{clipId:guid}/content", StaffContentAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPut("/api/audio-clips/{clipId:guid}/sharing", ShareAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPut("/api/audio-clips/{clipId:guid}/star", StarAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapDelete("/api/audio-clips/{clipId:guid}", DeleteAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPut("/api/students/{studentId:guid}/audio-consent", AdminConsentAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapGet("/api/audio-clips/usage", UsageAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        app.MapGet("/api/guardian/me/students/{studentId:guid}/audio-clips", GuardianListAsync)
            .RequireAuthorization(AuthorizationPolicies.GuardianOnly);
        app.MapGet("/api/guardian/me/audio-clips/{clipId:guid}/content", GuardianContentAsync)
            .RequireAuthorization(AuthorizationPolicies.GuardianOnly);
        app.MapPut("/api/guardian/me/students/{studentId:guid}/audio-consent", GuardianConsentAsync)
            .RequireAuthorization(AuthorizationPolicies.GuardianOnly);
    }

    private static async Task<IResult> CreateAsync(
        Guid lessonId,
        IFormFile? file,
        [FromForm] string? pieceTitle,
        [FromForm] int? durationSeconds,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        AudioClipStore store,
        IConfiguration config,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (AuthContext.IsAdmin(principal))
            throw new ForbiddenException("Ses kaydını yalnızca dersin öğretmeni alabilir.");
        var teacherId = await ProgressAuthorization.EnsureLessonAccessAsync(lessonId, principal, db)
            ?? throw new ForbiddenException("Ses kaydını yalnızca dersin öğretmeni alabilir.");

        if (file is null || file.Length == 0)
            throw Invalid("file", "Ses kaydı gönderilmedi.");
        // Sınır belleğe almadan ÖNCE kontrol edilir (ScoreFiles ile aynı gerekçe).
        if (file.Length > AudioClip.MaxBytes)
            throw Invalid("file", "Kayıt en fazla 400 KB olabilir. Kaydı 60 saniyenin altında tutun.");

        var lesson = await db.Lessons.AsNoTracking().SingleAsync(item => item.Id == lessonId, cancellationToken);
        var student = await db.Students.AsNoTracking().SingleAsync(item => item.Id == lesson.StudentId, cancellationToken);
        if (!student.AudioRecordingConsent)
            throw new ConflictException("Bu öğrenci için ses kaydı rızası yok. Rıza veli portalından veya yöneticiden açılır.");

        var now = clock.UtcNow;
        var quota = TermQuota(config);
        var used = await CountThisTermAsync(student.Id, db, clock, cancellationToken);
        if (used >= quota)
            throw new ConflictException($"Bu dönem için {quota} kayıt sınırına ulaşıldı. Eski bir kaydı silerek yer açabilirsiniz.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var content = buffer.ToArray();
        var format = AudioFormat.Detect(content)
            ?? throw Invalid("file", "Dosya desteklenen bir ses kaydı değil (WebM, Ogg veya M4A).");

        var clip = AudioClip.Create(
            student.Id, teacherId, lessonId, pieceTitle, format, content.Length,
            durationSeconds ?? 0, AuthContext.GetUserId(principal), now);

        // Önce dosya, sonra satır: satır yazılamazsa dosya hemen silinir; süreç arada düşerse
        // geride kalan yetim dosyayı gece bakımı temizler. Tersi (satır var, dosya yok) bozuk
        // bir oynatıcı gösterirdi.
        await store.SaveAsync(clip.FileName, content, cancellationToken);
        try
        {
            db.AudioClips.Add(clip);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            store.Delete(clip.FileName);
            throw;
        }

        var teacherNames = await TeacherNamesAsync([teacherId], db);
        return Results.Created($"/api/audio-clips/{clip.Id}", ToResponse(clip, teacherNames));
    }

    private static async Task<IResult> StaffListAsync(
        Guid studentId, ClaimsPrincipal principal, AbderaDbContext db, IConfiguration config, IClock clock,
        CancellationToken cancellationToken)
    {
        await ProgressAuthorization.EnsureStudentAccessAsync(studentId, principal, db);
        var student = await db.Students.AsNoTracking().SingleAsync(item => item.Id == studentId, cancellationToken);
        var clips = await db.AudioClips.AsNoTracking()
            .Where(clip => clip.StudentId == studentId)
            .OrderByDescending(clip => clip.CreatedAt)
            .ToListAsync(cancellationToken);
        var teacherNames = await TeacherNamesAsync(clips.Select(clip => clip.TeacherId), db);

        return Results.Ok(new StudentClipsResponse(
            studentId,
            student.AudioRecordingConsent,
            student.AudioConsentUpdatedAt,
            TermQuota(config),
            await CountThisTermAsync(studentId, db, clock, cancellationToken),
            clips.Select(clip => ToResponse(clip, teacherNames)).ToList()));
    }

    private static async Task<IResult> StaffContentAsync(
        Guid clipId, HttpContext http, ClaimsPrincipal principal, AbderaDbContext db, AudioClipStore store)
    {
        var clip = await FindForStaffAsync(clipId, principal, db);
        return Serve(clip, http, store);
    }

    private static async Task<IResult> ShareAsync(
        Guid clipId, ShareRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var clip = await FindForStaffAsync(clipId, principal, db, tracked: true);
        if (clip.SharedWithGuardian == request.Shared)
            return Results.Ok(await ToResponseAsync(clip, db));

        var actorId = AuthContext.GetUserId(principal);
        clip.SetSharedWithGuardian(request.Shared, actorId, clock.UtcNow);
        db.AuditLogs.Add(AuditLog.Record(
            actorId, request.Shared ? "audio_clip.shared" : "audio_clip.unshared", nameof(AudioClip), clip.Id, clock.UtcNow,
            beforeJson: JsonSerializer.Serialize(new { sharedWithGuardian = !request.Shared }),
            afterJson: JsonSerializer.Serialize(new { sharedWithGuardian = request.Shared, clip.StudentId })));
        await db.SaveChangesAsync();
        return Results.Ok(await ToResponseAsync(clip, db));
    }

    private static async Task<IResult> StarAsync(
        Guid clipId, StarRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var clip = await FindForStaffAsync(clipId, principal, db, tracked: true);
        clip.SetStarred(request.Starred, clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(await ToResponseAsync(clip, db));
    }

    private static async Task<IResult> DeleteAsync(
        Guid clipId, ClaimsPrincipal principal, AbderaDbContext db, AudioClipStore store, IClock clock)
    {
        var clip = await FindForStaffAsync(clipId, principal, db, tracked: true);
        db.AudioClips.Remove(clip);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "audio_clip.deleted", nameof(AudioClip), clip.Id, clock.UtcNow,
            beforeJson: JsonSerializer.Serialize(new { clip.StudentId, clip.LessonId, clip.PieceTitle, clip.SizeBytes, clip.SharedWithGuardian })));
        await db.SaveChangesAsync();
        // Satır gittikten sonra dosya: silme yarıda kalırsa yetim dosyayı gece bakımı temizler.
        store.Delete(clip.FileName);
        return Results.NoContent();
    }

    private static async Task<IResult> AdminConsentAsync(
        Guid studentId, ConsentRequest request, ClaimsPrincipal principal, AbderaDbContext db, AudioClipStore store, IClock clock)
    {
        if (!await db.Students.AnyAsync(student => student.Id == studentId))
            throw new NotFoundException("Öğrenci bulunamadı.");
        return Results.Ok(await SetConsentAsync(studentId, request.Consent, AuthContext.GetUserId(principal), "admin", db, store, clock));
    }

    private static async Task<IResult> GuardianConsentAsync(
        Guid studentId, ConsentRequest request, ClaimsPrincipal principal, AbderaDbContext db, AudioClipStore store, IClock clock)
    {
        var guardianId = AuthContext.GetUserId(principal);
        await EnsureGuardianLinkAsync(studentId, guardianId, db);
        return Results.Ok(await SetConsentAsync(studentId, request.Consent, guardianId, "guardian", db, store, clock));
    }

    // Rıza değişikliği CLAUDE.md gereği audit'e yazılır. Kapatma kayıtları siler; dosyalar
    // satırlar kalıcı olarak gittikten SONRA diskten kaldırılır.
    internal static async Task<ConsentResponse> SetConsentAsync(
        Guid studentId, bool consent, Guid actorId, string source, AbderaDbContext db, AudioClipStore store, IClock clock)
    {
        var student = await db.Students.SingleAsync(item => item.Id == studentId);
        if (student.AudioRecordingConsent == consent)
            return new ConsentResponse(studentId, consent, 0);

        var now = clock.UtcNow;
        var clips = consent
            ? []
            : await db.AudioClips.Where(clip => clip.StudentId == studentId).ToListAsync();
        student.SetAudioRecordingConsent(consent, now);
        db.AudioClips.RemoveRange(clips);
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "student.audio_consent_changed", nameof(Student), studentId, now,
            beforeJson: JsonSerializer.Serialize(new { audioRecordingConsent = !consent }),
            afterJson: JsonSerializer.Serialize(new { audioRecordingConsent = consent, source, deletedClips = clips.Count })));
        await db.SaveChangesAsync();
        store.DeleteMany(clips.Select(clip => clip.FileName));
        return new ConsentResponse(studentId, consent, clips.Count);
    }

    private static async Task<IResult> GuardianListAsync(
        Guid studentId, ClaimsPrincipal principal, AbderaDbContext db, CancellationToken cancellationToken)
    {
        await EnsureGuardianLinkAsync(studentId, AuthContext.GetUserId(principal), db);
        var student = await db.Students.AsNoTracking().SingleAsync(item => item.Id == studentId, cancellationToken);
        var clips = await db.AudioClips.AsNoTracking()
            .Where(clip => clip.StudentId == studentId && clip.SharedWithGuardianAt != null)
            .OrderByDescending(clip => clip.CreatedAt)
            .ToListAsync(cancellationToken);
        var teacherNames = await TeacherNamesAsync(clips.Select(clip => clip.TeacherId), db);
        return Results.Ok(new GuardianClipsResponse(
            studentId,
            student.AudioRecordingConsent,
            clips.Select(clip => new GuardianClipResponse(
                clip.Id, clip.PieceTitle, NameOf(clip.TeacherId, teacherNames), clip.DurationSeconds, clip.ContentType, clip.CreatedAt))
                .ToList()));
    }

    private static async Task<IResult> GuardianContentAsync(
        Guid clipId, HttpContext http, ClaimsPrincipal principal, AbderaDbContext db, AudioClipStore store)
    {
        var clip = await db.AudioClips.AsNoTracking().SingleOrDefaultAsync(item => item.Id == clipId);
        // Paylaşılmamış kayıt veliye "yok" görünür; varlığı bile sızmaz.
        if (clip is null || !clip.SharedWithGuardian)
            throw new NotFoundException("Kayıt bulunamadı.");
        await EnsureGuardianLinkAsync(clip.StudentId, AuthContext.GetUserId(principal), db);
        return Serve(clip, http, store);
    }

    private static async Task<IResult> UsageAsync(AbderaDbContext db, AudioClipStore store, IClock clock)
    {
        var since = clock.UtcNow.AddDays(-30);
        var totals = await db.AudioClips.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Bytes = group.Sum(clip => (long)clip.SizeBytes),
                Count = group.Count(),
                Recent = group.Where(clip => clip.CreatedAt >= since).Sum(clip => (long)clip.SizeBytes),
                NotBackedUp = group.Count(clip => clip.BackedUpAt == null),
            })
            .SingleOrDefaultAsync();
        var disk = store.Usage();
        return Results.Ok(new UsageResponse(
            totals?.Bytes ?? 0, totals?.Count ?? 0, totals?.Recent ?? 0, totals?.NotBackedUp ?? 0, disk.Bytes, disk.Files));
    }

    private static IResult Serve(AudioClip clip, HttpContext http, AudioClipStore store)
    {
        var path = store.ExistingPath(clip.FileName)
            ?? throw new NotFoundException("Ses dosyası bulunamadı.");
        // Dosya hiç değişmez (yeni kayıt = yeni ad): tarayıcı bir kez indirip saklar, tekrar
        // dinlemede sunucuya trafik gitmez. private: oturuma bağlı içerik paylaşılan önbelleğe girmesin.
        http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return TypedResults.PhysicalFile(Path.GetFullPath(path), clip.ContentType, enableRangeProcessing: true);
    }

    private static async Task<AudioClip> FindForStaffAsync(
        Guid clipId, ClaimsPrincipal principal, AbderaDbContext db, bool tracked = false)
    {
        var query = tracked ? db.AudioClips : db.AudioClips.AsNoTracking();
        var clip = await query.SingleOrDefaultAsync(item => item.Id == clipId)
            ?? throw new NotFoundException("Kayıt bulunamadı.");
        await ProgressAuthorization.EnsureStudentAccessAsync(clip.StudentId, principal, db);
        return clip;
    }

    private static async Task EnsureGuardianLinkAsync(Guid studentId, Guid guardianId, AbderaDbContext db)
    {
        if (!await db.StudentGuardians.AnyAsync(link => link.StudentId == studentId && link.GuardianId == guardianId))
            throw new ForbiddenException("Bu öğrencinin kayıtlarına erişemezsiniz.");
    }

    private static int TermQuota(IConfiguration config) =>
        Math.Max(1, config.GetValue("AudioClips:MaxPerStudentPerTerm", DefaultTermQuota));

    private static async Task<int> CountThisTermAsync(
        Guid studentId, AbderaDbContext db, IClock clock, CancellationToken cancellationToken)
    {
        var local = clock.ToSchoolLocal(clock.UtcNow);
        var term = ReportTerm.Containing(DateOnly.FromDateTime(local.DateTime));
        var termStart = new DateTimeOffset(term.Start.ToDateTime(TimeOnly.MinValue), local.Offset).ToUniversalTime();
        return await db.AudioClips.CountAsync(
            clip => clip.StudentId == studentId && clip.CreatedAt >= termStart, cancellationToken);
    }

    private static async Task<Dictionary<Guid, string>> TeacherNamesAsync(IEnumerable<Guid?> teacherIds, AbderaDbContext db)
    {
        var ids = teacherIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Teachers.AsNoTracking()
            .Where(teacher => ids.Contains(teacher.Id))
            .ToDictionaryAsync(teacher => teacher.Id, teacher => teacher.FirstName + " " + teacher.LastName);
    }

    private static string? NameOf(Guid? teacherId, Dictionary<Guid, string> names) =>
        teacherId is { } id && names.TryGetValue(id, out var name) ? name : null;

    private static async Task<ClipResponse> ToResponseAsync(AudioClip clip, AbderaDbContext db) =>
        ToResponse(clip, await TeacherNamesAsync([clip.TeacherId], db));

    private static ClipResponse ToResponse(AudioClip clip, Dictionary<Guid, string> teacherNames) => new(
        clip.Id, clip.StudentId, clip.LessonId, clip.TeacherId, NameOf(clip.TeacherId, teacherNames), clip.PieceTitle,
        clip.ContentType, clip.SizeBytes, clip.DurationSeconds, clip.Starred, clip.SharedWithGuardian, clip.CreatedAt);

    private static ValidationFailedException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
