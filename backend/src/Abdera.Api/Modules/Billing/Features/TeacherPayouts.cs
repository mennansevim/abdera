using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// Öğretmenlere haftalık ders ödemesi (docs/10-decisions.md N1). Kullanıcı kuralı:
// "her cumartesi tamamlanan derslerin ödemesini yapıyorum".
//
// İş iki parçadır ve ikisi de burada: haftanın TABLOSUNU görmek (hangi öğretmenin kaç
// tamamlanmış dersi var, tutarı ne, ödendi mi) ve o haftanın ödemesini GİDER olarak
// yazmak. Ödeme kaydı `expenses` tablosuna Maaş kategorisiyle düşer, böylece Giderler
// ekranındaki her toplam onu kendiliğinden sayar - ayrı bir "öğretmen gideri" defteri
// açılmadı.
public static class TeacherPayouts
{
    public record RateRequest(decimal AmountPerLesson, string? Currency);

    public record CreateRequest(
        Guid TeacherId,
        DateOnly WeekStart,
        int ExpectedLessonCount,
        decimal ExpectedAmount,
        decimal? AgreedAmount,
        DateOnly? PaidOn,
        string? Note);

    public record PayoutResponse(
        Guid Id, Guid TeacherId, DateOnly WeekStart, DateOnly WeekEnd, int LessonCount,
        decimal RatePerLesson, decimal ComputedAmount, decimal Amount, string Currency,
        DateOnly PaidOn, string? Note, Guid ExpenseId, DateTimeOffset CreatedAt);

    public record TeacherWeekRow(
        Guid TeacherId, string FirstName, string LastName, TeacherStatus Status,
        int CompletedLessons, decimal? RatePerLesson, decimal? ComputedAmount, string Currency,
        PayoutResponse? Payout);

    public record WeekResponse(
        DateOnly WeekStart, DateOnly WeekEnd, DateOnly PayDay, bool IsCurrentWeek, bool IsClosed,
        int TotalCompletedLessons, decimal PaidTotal, decimal PayableTotal, string Currency,
        List<TeacherWeekRow> Teachers);

    public record RateResponse(Guid TeacherId, decimal AmountPerLesson, string Currency, DateTimeOffset UpdatedAt);

    public static void MapTeacherPayouts(this IEndpointRouteBuilder app)
    {
        // Maaş verisi tamamen Admin'e özel (docs/04-permissions.md) - bir öğretmenin
        // başka bir öğretmenin ücretini görmesi söz konusu değil.
        var group = app.MapGroup("/api/teacher-payouts").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("/week", WeekAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/rates/{teacherId:guid}", SetRateAsync);
    }

    private static async Task<IResult> WeekAsync(DateOnly? weekStart, AbderaDbContext db, IClock clock)
    {
        var todayLocal = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var currentWeek = TeacherPayWeek.Containing(todayLocal);
        var week = TeacherPayWeek.Containing(weekStart ?? todayLocal);

        var teachers = await db.Teachers.AsNoTracking().ToListAsync();
        var rates = await db.TeacherPayRates.AsNoTracking().ToListAsync();
        var payouts = await db.TeacherWeeklyPayouts.AsNoTracking()
            .Where(payout => payout.WeekStart == week.Start).ToListAsync();
        var counts = await CountCompletedByTeacherAsync(db, clock, week);

        var rows = teachers
            // Pasif öğretmen ancak o hafta dersi tamamlanmışsa ya da ödemesi yapılmışsa
            // listede kalır - ayrılmış bir öğretmenin son haftası ödenebilmeli, ama liste
            // her hafta eski öğretmenlerle şişmemeli.
            .Where(teacher => teacher.Status == TeacherStatus.Active
                || counts.ContainsKey(teacher.Id)
                || payouts.Any(payout => payout.TeacherId == teacher.Id))
            .Select(teacher =>
            {
                var rate = rates.SingleOrDefault(item => item.TeacherId == teacher.Id);
                var completed = counts.GetValueOrDefault(teacher.Id);
                var payout = payouts.SingleOrDefault(item => item.TeacherId == teacher.Id);
                return new TeacherWeekRow(
                    teacher.Id, teacher.FirstName, teacher.LastName, teacher.Status,
                    completed,
                    rate?.AmountPerLesson,
                    rate?.ComputeWeeklyAmount(completed),
                    payout?.Currency ?? rate?.Currency ?? "TRY",
                    payout is null ? null : ToResponse(payout));
            })
            .OrderBy(row => $"{row.FirstName} {row.LastName}", StringComparer.Create(TurkishCulture, ignoreCase: true))
            .ToList();

        return Results.Ok(new WeekResponse(
            week.Start,
            week.End,
            week.End,
            week == currentWeek,
            week.End < todayLocal,
            rows.Sum(row => row.CompletedLessons),
            rows.Sum(row => row.Payout?.Amount ?? 0m),
            rows.Where(row => row.Payout is null).Sum(row => row.ComputedAmount ?? 0m),
            rows.Select(row => row.Currency).FirstOrDefault() ?? "TRY",
            rows));
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var week = TeacherPayWeek.Containing(request.WeekStart);
        var todayLocal = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        if (week.Start > todayLocal)
            throw Invalid("weekStart", "Henüz başlamamış bir haftanın ödemesi yapılamaz.");

        var teacher = await db.Teachers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.TeacherId)
            ?? throw new NotFoundException("Öğretmen bulunamadı.");

        if (await db.TeacherWeeklyPayouts.AnyAsync(payout =>
                payout.TeacherId == teacher.Id && payout.WeekStart == week.Start))
            throw new ConflictException($"{teacher.FirstName} {teacher.LastName} için bu haftanın ödemesi zaten yapılmış.");

        var rate = await db.TeacherPayRates.SingleOrDefaultAsync(item => item.TeacherId == teacher.Id)
            ?? throw Invalid("teacherId", "Bu öğretmenin ders başı ücreti girilmemiş. Önce ücreti kaydet.");

        var counts = await CountCompletedByTeacherAsync(db, clock, week, teacher.Id);
        var lessonCount = counts.GetValueOrDefault(teacher.Id);
        if (lessonCount == 0)
            throw Invalid("weekStart", "Bu haftada tamamlanmış ders yok; ödenecek bir tutar çıkmıyor.");

        // Tutarı SUNUCU hesaplar; istemci yalnızca ekranda gördüğünü teyit eder (CLAUDE.md
        // peşin ödeme kuralının aynısı). Ekran açıkken bir derse yoklama girilmişse sayı
        // değişmiş olur - o durumda ödeme sessizce farklı bir tutarla geçmemeli.
        var computedAmount = rate.ComputeWeeklyAmount(lessonCount);
        if (request.ExpectedLessonCount != lessonCount || request.ExpectedAmount != computedAmount)
            throw new ConflictException(
                $"Bu hafta {lessonCount} tamamlanmış ders ve {Money(computedAmount, rate.Currency)} görünüyor " +
                $"(ekranda {request.ExpectedLessonCount} ders / {Money(request.ExpectedAmount, rate.Currency)} vardı). " +
                "Listeyi yenileyip tekrar dene.");

        // Tek istisna yöneticinin açıkça girdiği tutar (küsürat/elden yuvarlama) - hesap yine
        // önce sunucuda yapılır, girilen tutar audit'e hesaplananla birlikte yazılır.
        var amount = request.AgreedAmount ?? computedAmount;
        if (amount <= 0) throw Invalid("agreedAmount", "Ödeme tutarı pozitif olmalı.");

        var paidOn = request.PaidOn ?? week.End;
        if (paidOn < week.Start) throw Invalid("paidOn", "Ödeme tarihi haftanın başlangıcından önce olamaz.");

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var description = $"Haftalık ders ödemesi — {teacher.FirstName} {teacher.LastName} ({WeekLabel(week)})";
        var expense = Expense.Create(
            ExpenseCategory.Salary, description, amount, rate.Currency, paidOn, request.Note, actorId, now);
        var payout = TeacherWeeklyPayout.Create(
            teacher.Id, week, lessonCount, rate.AmountPerLesson, computedAmount, amount, rate.Currency,
            paidOn, request.Note, expense.Id, actorId, now);

        db.Expenses.Add(expense);
        db.TeacherWeeklyPayouts.Add(payout);
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "teacher_payout.created", nameof(TeacherWeeklyPayout), payout.Id, now,
            afterJson: JsonSerializer.Serialize(new
            {
                teacherId = payout.TeacherId,
                weekStart = payout.WeekStart,
                weekEnd = payout.WeekEnd,
                lessonCount = payout.LessonCount,
                ratePerLesson = payout.RatePerLesson,
                computedAmount = payout.ComputedAmount,
                agreedAmount = request.AgreedAmount,
                amount = payout.Amount,
                currency = payout.Currency,
                paidOn = payout.PaidOn,
                expenseId = payout.ExpenseId,
            })));

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // İki yönetici aynı anda "Ödemeyi kaydet" derse ikincisi tekillik kısıtına
            // takılır; kullanıcıya 500 değil "zaten ödendi" demek doğrusu. Filtre yerine
            // gövdede kontrol ediliyor - `catch ... when (await ...)` C#'ta geçersiz.
            if (await AlreadyPaidAsync(db, teacher.Id, week.Start))
                throw new ConflictException($"{teacher.FirstName} {teacher.LastName} için bu haftanın ödemesi zaten yapılmış.");
            throw;
        }

        return Results.Created($"/api/teacher-payouts/{payout.Id}", ToResponse(payout));
    }

    private static async Task<IResult> SetRateAsync(
        Guid teacherId, RateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        if (request.AmountPerLesson <= 0) throw Invalid("amountPerLesson", "Ders başı ücret pozitif olmalı.");

        var teacher = await db.Teachers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == teacherId)
            ?? throw new NotFoundException("Öğretmen bulunamadı.");

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var rate = await db.TeacherPayRates.SingleOrDefaultAsync(item => item.TeacherId == teacherId);
        string? beforeJson = null;
        if (rate is null)
        {
            rate = TeacherPayRate.Create(teacher.Id, request.AmountPerLesson, request.Currency ?? "TRY", actorId, now);
            db.TeacherPayRates.Add(rate);
        }
        else
        {
            beforeJson = JsonSerializer.Serialize(new { amountPerLesson = rate.AmountPerLesson, currency = rate.Currency });
            rate.ChangeAmount(request.AmountPerLesson, request.Currency ?? rate.Currency, actorId, now);
        }

        db.AuditLogs.Add(AuditLog.Record(
            actorId, "teacher_pay_rate.set", nameof(TeacherPayRate), rate.Id, now,
            beforeJson: beforeJson,
            afterJson: JsonSerializer.Serialize(new
            {
                teacherId = rate.TeacherId,
                amountPerLesson = rate.AmountPerLesson,
                currency = rate.Currency,
            })));
        await db.SaveChangesAsync();

        return Results.Ok(new RateResponse(rate.TeacherId, rate.AmountPerLesson, rate.Currency, rate.UpdatedAt));
    }

    // Bir ödeme haftasında tamamlanmış ders sayısı, öğretmen kırılımında. Yalnızca COMPLETED
    // sayılır: iptal, ertelenmiş (eski satır) ve henüz yoklaması girilmemiş ders ödenmez.
    // Telafi dersi de yoklaması girildiğinde COMPLETED olur, bu yüzden ayrıca sayılmasına
    // gerek yok. Billing'in Lessons'ı doğrudan sayması MakeupCredits.cs'teki haftalık ders
    // kotasıyla aynı kalıptır - join değil, açık bir sayım sorgusu.
    private static async Task<Dictionary<Guid, int>> CountCompletedByTeacherAsync(
        AbderaDbContext db, IClock clock, TeacherPayWeek week, Guid? teacherId = null)
    {
        var fromAt = LessonGenerator.ToUtcInstant(week.Start, TimeOnly.MinValue, clock.SchoolTimeZone);
        var toAt = LessonGenerator.ToUtcInstant(week.ExclusiveEnd, TimeOnly.MinValue, clock.SchoolTimeZone);

        var query = db.Lessons.AsNoTracking()
            .Where(lesson => lesson.Status == LessonStatus.Completed
                && lesson.StartAt >= fromAt && lesson.StartAt < toAt);
        if (teacherId is { } id) query = query.Where(lesson => lesson.TeacherId == id);

        var rows = await query
            .GroupBy(lesson => lesson.TeacherId)
            .Select(group => new { TeacherId = group.Key, Count = group.Count() })
            .ToListAsync();

        return rows.ToDictionary(row => row.TeacherId, row => row.Count);
    }

    private static async Task<bool> AlreadyPaidAsync(AbderaDbContext db, Guid teacherId, DateOnly weekStart)
    {
        db.ChangeTracker.Clear();
        return await db.TeacherWeeklyPayouts.AsNoTracking()
            .AnyAsync(payout => payout.TeacherId == teacherId && payout.WeekStart == weekStart);
    }

    // CLAUDE.md: kullanıcıya görünen metinde biçimlendirme açıkça tr-TR ile yapılır
    // (JSON'un tersine - orada her zaman JsonSerializer).
    private static CultureInfo TurkishCulture => CultureInfo.GetCultureInfo("tr-TR");

    private static string Money(decimal amount, string currency) =>
        $"{amount.ToString("N2", TurkishCulture)} {currency}";

    private static string WeekLabel(TeacherPayWeek week) =>
        $"{week.Start.ToString("d MMM", TurkishCulture)} – {week.End.ToString("d MMM yyyy", TurkishCulture)}";

    private static ValidationFailedException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static PayoutResponse ToResponse(TeacherWeeklyPayout payout) => new(
        payout.Id, payout.TeacherId, payout.WeekStart, payout.WeekEnd, payout.LessonCount,
        payout.RatePerLesson, payout.ComputedAmount, payout.Amount, payout.Currency,
        payout.PaidOn, payout.Note, payout.ExpenseId, payout.CreatedAt);
}
