using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Infrastructure;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Abdera.Api.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// Grup dersleri (Resim gibi): yönetici sınıfı (öğretmen + kurs + gün/saat + süre + kapasite)
// açar, öğrencileri altına ekler. Her üye için Grup türünde bir kurs kaydı ve gruba bağlı
// haftalık seri açılır - yoklama, telafi, aidat mevcut öğrenci bazlı akışlarla çalışır.
// Üyeyi gruptan çıkarmak kurs kaydını bitirmektir (POST /api/students/{id}/enrollments/{id}/end):
// ayrılma nedeni, gelecekteki derslerin ve hatırlatmaların temizlenmesi orada zaten var.
public static class LessonGroups
{
    public record CreateRequest(
        string Name, Guid TeacherId, Guid InstrumentId, DayOfWeek DayOfWeek, TimeOnly StartTime,
        DateOnly EffectiveFrom, int? DurationMinutes = null, int? Capacity = null);

    public record UpdateRequest(string? Name, int? Capacity);

    // StartFrom boşsa bugünden (ya da grup daha başlamadıysa grubun başlangıcından) itibaren.
    public record AddMemberRequest(Guid StudentId, DateOnly? StartFrom = null);

    public record MemberResponse(Guid StudentId, string StudentName, Guid EnrollmentId, Guid SeriesId, DateOnly JoinedOn);

    public record LessonGroupResponse(
        Guid Id, string Name, Guid TeacherId, string TeacherName, Guid InstrumentId, string InstrumentName,
        DayOfWeek DayOfWeek, TimeOnly StartTime, int DurationMinutes, int Capacity, DateOnly EffectiveFrom,
        LessonGroupStatus Status, DateOnly? EndedOn, IReadOnlyList<MemberResponse> Members);

    public record AddMemberResponse(LessonGroupResponse Group, LessonSeriesFeatures.GenerationSummary Generation);

    public static void MapLessonGroups(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/lesson-groups").RequireModule(ModuleKeys.Groups);
        // Öğretmen kendi gruplarını görür (sınıf listesi); kurmak/değiştirmek yöneticinin işi.
        group.MapGet("", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        group.MapPost("", CreateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPatch("/{groupId:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPost("/{groupId:guid}/members", AddMemberAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPost("/{groupId:guid}/end", EndAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(bool? includeEnded, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        var query = db.LessonGroups.AsQueryable();
        if (includeEnded != true) query = query.Where(g => g.Status == LessonGroupStatus.Active);
        if (teacherScope is { } teacherId) query = query.Where(g => g.TeacherId == teacherId);

        var groups = await query.OrderBy(g => g.DayOfWeek).ThenBy(g => g.StartTime).ThenBy(g => g.Name).ToListAsync();
        return Results.Ok(await ToResponsesAsync(groups, db));
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var teacher = await db.Teachers.SingleOrDefaultAsync(t => t.Id == request.TeacherId)
            ?? throw new NotFoundException("Öğretmen bulunamadı.");
        if (teacher.Status != TeacherStatus.Active)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["teacherId"] = ["Bu öğretmen aktif değil."] });
        if (!await db.Instruments.AnyAsync(i => i.Id == request.InstrumentId))
            throw new NotFoundException("Kurs bulunamadı.");
        if (!await db.TeacherInstruments.AnyAsync(ti => ti.TeacherId == request.TeacherId && ti.InstrumentId == request.InstrumentId))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["teacherId"] = ["Bu öğretmen bu kursu vermiyor."] });

        var lessonGroup = LessonGroup.Create(
            request.Name, request.TeacherId, request.InstrumentId, request.DayOfWeek, request.StartTime,
            request.DurationMinutes ?? LessonGroup.DefaultDurationMinutes, request.Capacity ?? LessonGroup.DefaultCapacity,
            request.EffectiveFrom, clock.UtcNow);

        await LessonSeriesFeatures.EnsureWithinAvailabilityAsync(
            lessonGroup.TeacherId, lessonGroup.DayOfWeek, lessonGroup.StartTime, lessonGroup.EndTime, db);
        // Grup saati öğretmenin diğer kurslarıyla (bireysel seriler + diğer grupları) çakışamaz.
        await LessonSeriesFeatures.EnsureTeacherSlotFreeAsync(
            lessonGroup.TeacherId, lessonGroup.DayOfWeek, lessonGroup.StartTime, lessonGroup.EndTime,
            lessonGroup.EffectiveFrom, null, db);

        db.LessonGroups.Add(lessonGroup);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "lesson_group.created", nameof(LessonGroup), lessonGroup.Id, clock.UtcNow,
            afterJson: Serialize(lessonGroup)));
        await db.SaveChangesAsync();

        return Results.Created($"/api/lesson-groups/{lessonGroup.Id}", (await ToResponsesAsync([lessonGroup], db)).Single());
    }

    private static async Task<IResult> UpdateAsync(Guid groupId, UpdateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var lessonGroup = await db.LessonGroups.SingleOrDefaultAsync(g => g.Id == groupId)
            ?? throw new NotFoundException("Grup bulunamadı.");
        var before = Serialize(lessonGroup);

        if (request.Name is not null) lessonGroup.Rename(request.Name, clock.UtcNow);
        if (request.Capacity is { } capacity)
        {
            var activeMembers = await CountActiveMembersAsync(groupId, db);
            lessonGroup.SetCapacity(capacity, activeMembers, clock.UtcNow);
        }

        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "lesson_group.updated", nameof(LessonGroup), lessonGroup.Id, clock.UtcNow,
            beforeJson: before, afterJson: Serialize(lessonGroup)));
        await db.SaveChangesAsync();
        return Results.Ok((await ToResponsesAsync([lessonGroup], db)).Single());
    }

    private static async Task<IResult> AddMemberAsync(
        Guid groupId, AddMemberRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock,
        IConfiguration config, INotificationScheduler scheduler)
    {
        var lessonGroup = await db.LessonGroups.SingleOrDefaultAsync(g => g.Id == groupId)
            ?? throw new NotFoundException("Grup bulunamadı.");
        var student = await db.Students.SingleOrDefaultAsync(s => s.Id == request.StudentId)
            ?? throw new NotFoundException("Öğrenci bulunamadı.");
        if (student.Status != StudentStatus.Active)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["studentId"] = ["Bu öğrenci aktif değil."] });

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        var defaultStart = lessonGroup.EffectiveFrom > today ? lessonGroup.EffectiveFrom : today;
        var startFrom = request.StartFrom ?? defaultStart;
        if (startFrom < lessonGroup.EffectiveFrom)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["startFrom"] = ["Öğrenci gruptan önce başlayamaz."] });

        var alreadyMember = await db.LessonSeries
            .Where(s => s.LessonGroupId == groupId && s.Status == LessonSeriesStatus.Active)
            .Join(db.Enrollments, s => s.EnrollmentId, e => e.Id, (s, e) => e.StudentId)
            .AnyAsync(studentId => studentId == request.StudentId);
        if (alreadyMember) throw new ConflictException("Öğrenci zaten bu grupta.");

        lessonGroup.EnsureCanAdmit(await CountActiveMembersAsync(groupId, db), now);

        // Öğrencinin bu öğretmen + kurs için açık bir kaydı varsa (ör. önce kayıt açılıp program
        // girilmemiş) o kullanılır; yoksa Grup türünde yeni kayıt açılır. Aidat tutarı Grup
        // tarifesinden gelsin diye ders türü Grup'a çekilir.
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(e =>
            e.StudentId == request.StudentId && e.TeacherId == lessonGroup.TeacherId &&
            e.InstrumentId == lessonGroup.InstrumentId && e.Status == EnrollmentStatus.Active);
        var enrollmentCreated = enrollment is null;
        if (enrollment is null)
        {
            enrollment = Enrollment.Create(
                request.StudentId, lessonGroup.TeacherId, lessonGroup.InstrumentId, CourseKind.Group, startFrom, now);
            db.Enrollments.Add(enrollment);
            db.AuditLogs.Add(AuditLog.Record(
                AuthContext.GetUserId(principal), "enrollment.created", nameof(Enrollment), enrollment.Id, now,
                afterJson: JsonSerializer.Serialize(new
                {
                    enrollment.StudentId,
                    enrollment.TeacherId,
                    enrollment.InstrumentId,
                    CourseKind = enrollment.CourseKind.ToString(),
                    enrollment.StartedAt,
                    Status = enrollment.Status.ToString(),
                    LessonGroupId = lessonGroup.Id,
                })));
        }
        else if (enrollment.CourseKind != CourseKind.Group)
        {
            var before = JsonSerializer.Serialize(new { CourseKind = enrollment.CourseKind.ToString() });
            enrollment.SetCourseKind(CourseKind.Group, now);
            db.AuditLogs.Add(AuditLog.Record(
                AuthContext.GetUserId(principal), "enrollment.updated", nameof(Enrollment), enrollment.Id, now,
                beforeJson: before, afterJson: JsonSerializer.Serialize(new { CourseKind = enrollment.CourseKind.ToString() })));
            await EnrollmentReceivableRepricer.RepriceUnpaidAsync(enrollment, db, AuthContext.GetUserId(principal), now);
        }

        await LessonSeriesFeatures.EnsureStudentWeeklyLimitAsync(enrollment.StudentId, startFrom, null, db);
        await LessonSeriesFeatures.EnsureSingleSeriesPerInstrumentAsync(
            enrollment.StudentId, enrollment.InstrumentId, startFrom, null, db);
        await LessonSeriesFeatures.EnsureNoConflictAsync(
            enrollment, lessonGroup.DayOfWeek, lessonGroup.StartTime, lessonGroup.EndTime, startFrom, null, db,
            lessonGroupId: lessonGroup.Id);

        var series = LessonSeries.Create(
            enrollment.Id, lessonGroup.DayOfWeek, lessonGroup.StartTime, lessonGroup.DurationMinutes,
            startFrom, null, now, lessonGroup.Id);
        db.LessonSeries.Add(series);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "lesson_series.created", nameof(LessonSeries), series.Id, now,
            afterJson: LessonSeriesFeatures.SerializeSeries(series, enrollment.TeacherId, enrollment.StudentId)));
        await db.SaveChangesAsync();

        if (enrollmentCreated)
        {
            // Yeni kurs kaydında bu ayın aidatı hemen açılır (Enrollments.CreateAsync ile aynı kural).
            await EnrollmentReceivableOpener.OpenCurrentPeriodAsync(db, clock, enrollment.Id, AuthContext.GetUserId(principal));
        }

        var generation = await LessonSeriesFeatures.GenerateForSeriesAsync(series, enrollment, db, clock, config, scheduler);
        var response = (await ToResponsesAsync([lessonGroup], db)).Single();
        return Results.Created($"/api/lesson-groups/{lessonGroup.Id}/members/{enrollment.StudentId}", new AddMemberResponse(response, generation));
    }

    private static async Task<IResult> EndAsync(Guid groupId, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var lessonGroup = await db.LessonGroups.SingleOrDefaultAsync(g => g.Id == groupId)
            ?? throw new NotFoundException("Grup bulunamadı.");
        if (lessonGroup.Status == LessonGroupStatus.Ended) return Results.Ok((await ToResponsesAsync([lessonGroup], db)).Single());

        var activeMembers = await CountActiveMembersAsync(groupId, db);
        if (activeMembers > 0)
            throw new ConflictException($"Grupta {activeMembers} öğrenci var; önce öğrencileri gruptan çıkarın.");

        var before = Serialize(lessonGroup);
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        lessonGroup.End(today, clock.UtcNow);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "lesson_group.ended", nameof(LessonGroup), lessonGroup.Id, clock.UtcNow,
            beforeJson: before, afterJson: Serialize(lessonGroup)));
        await db.SaveChangesAsync();
        return Results.Ok((await ToResponsesAsync([lessonGroup], db)).Single());
    }

    private static Task<int> CountActiveMembersAsync(Guid groupId, AbderaDbContext db) =>
        db.LessonSeries.CountAsync(s => s.LessonGroupId == groupId && s.Status == LessonSeriesStatus.Active);

    private static async Task<List<LessonGroupResponse>> ToResponsesAsync(List<LessonGroup> groups, AbderaDbContext db)
    {
        var groupIds = groups.Select(g => g.Id).ToList();
        var teacherIds = groups.Select(g => g.TeacherId).Distinct().ToList();
        var instrumentIds = groups.Select(g => g.InstrumentId).Distinct().ToList();

        var teachers = await db.Teachers.Where(t => teacherIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.FirstName + " " + t.LastName);
        var instruments = await db.Instruments.Where(i => instrumentIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Name);

        // OrderBy projeksiyondan önce, anonim tipte (CLAUDE.md "çok tablolu sorgularda OrderBy").
        var members = await db.LessonSeries
            .Where(s => s.LessonGroupId != null && groupIds.Contains(s.LessonGroupId.Value) && s.Status == LessonSeriesStatus.Active)
            .Join(db.Enrollments, s => s.EnrollmentId, e => e.Id, (s, e) => new { Series = s, Enrollment = e })
            .Join(db.Students, x => x.Enrollment.StudentId, st => st.Id, (x, st) => new { x.Series, x.Enrollment, Student = st })
            .OrderBy(x => x.Student.FirstName).ThenBy(x => x.Student.LastName)
            .Select(x => new
            {
                GroupId = x.Series.LessonGroupId!.Value,
                Member = new MemberResponse(
                    x.Student.Id, x.Student.FirstName + " " + x.Student.LastName,
                    x.Enrollment.Id, x.Series.Id, x.Series.EffectiveFrom),
            })
            .ToListAsync();
        var membersByGroup = members.ToLookup(m => m.GroupId, m => m.Member);

        return groups.Select(g => new LessonGroupResponse(
            g.Id, g.Name, g.TeacherId, teachers.GetValueOrDefault(g.TeacherId, ""), g.InstrumentId,
            instruments.GetValueOrDefault(g.InstrumentId, ""), g.DayOfWeek, g.StartTime, g.DurationMinutes,
            g.Capacity, g.EffectiveFrom, g.Status, g.EndedOn, membersByGroup[g.Id].ToList())).ToList();
    }

    // CLAUDE.md: jsonb metni JsonSerializer ile kurulur.
    private static string Serialize(LessonGroup g) => JsonSerializer.Serialize(new
    {
        g.Name,
        g.TeacherId,
        g.InstrumentId,
        DayOfWeek = g.DayOfWeek.ToString(),
        StartTime = g.StartTime.ToString("HH:mm:ss"),
        g.DurationMinutes,
        g.Capacity,
        g.EffectiveFrom,
        Status = g.Status.ToString(),
        g.EndedOn,
    });
}
