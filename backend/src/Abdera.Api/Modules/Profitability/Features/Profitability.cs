using System.Security.Claims;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Profitability.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Profitability.Features;

// Gelir ve gider ekranının "Kârlılık" sekmesi (docs/10-decisions.md V). Yalnızca yönetici.
// Ekran her açılışta güncel veriden hesaplanır; kayıt tutan tek şey yöneticinin fikirleri
// (growth_ideas) ve aylık yorum (profit_commentaries, bkz. ProfitCommentaries.cs).
//
// Fikirler para/takvim/rıza DEĞİŞTİRMEZ - yalnızca planlama notudur - bu yüzden audit_log'a
// yazılmaz ve yönetici yanlış girdiği fikri silebilir.
public static class Profitability
{
    public record MonthResponse(string Period, decimal Revenue, decimal TeacherCost, decimal FixedCost, decimal Net, bool IsCurrent, bool TeacherCostEstimated);

    public record UnitResponse(
        decimal IndividualPrice, decimal GroupPrice, int LessonsPerMonth, decimal AverageDiscountRate, decimal? AverageTeacherRate,
        decimal IndividualMargin, decimal GroupMarginPerStudent, decimal HourValueIndividual, decimal HourValueGroup, int GroupSizeForComparison,
        decimal AverageTenureMonths, bool TenureIsDefault, decimal LifetimeValue, int ActiveEnrollments, int EndedLast3Months, int EarlyChurnLast3Months);

    public record InstrumentResponse(Guid Id, string Name, int Individual, int Group, int Teachers, decimal? TeacherRate, decimal BookedHours, decimal? AvailableHours, decimal? Utilization, decimal? SpareHours);

    public record GapResponse(string Key, GapSeverity Severity, int Count, string Message);

    public record EvaluationResponse(decimal MonthlyNet, decimal OneTimeCost, decimal TwelveMonthNet, int? PaybackMonth, IReadOnlyList<decimal> Series, IdeaVerdict Verdict, IReadOnlyList<string> Reasons);

    public record IdeaResponse(
        Guid Id, GrowthIdeaKind Kind, string Title, string? Note, GrowthIdeaStatus Status,
        Guid? InstrumentId, string? BranchName, CourseKind? CourseKind, int? Students, decimal? TeacherRatePerLesson,
        decimal? DiscountPercent, int? DiscountMonths, int? AlreadyComingPercent, decimal? PriceChangePercent, int? LostStudents,
        decimal? MonthlyAmount, decimal? OneTimeCost, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, EvaluationResponse Evaluation);

    public record OverviewResponse(
        string Period, IReadOnlyList<MonthResponse> Months, UnitResponse Unit, IReadOnlyList<InstrumentResponse> Instruments,
        IReadOnlyList<GapResponse> Gaps, IReadOnlyList<string> Insights, IReadOnlyList<IdeaResponse> Ideas);

    public record IdeaRequest(
        GrowthIdeaKind Kind, string? Title, string? Note,
        Guid? InstrumentId, string? BranchName, CourseKind? CourseKind, int? Students, decimal? TeacherRatePerLesson,
        decimal? DiscountPercent, int? DiscountMonths, int? AlreadyComingPercent, decimal? PriceChangePercent, int? LostStudents,
        decimal? MonthlyAmount, decimal? OneTimeCost)
    {
        public GrowthIdeaParameters Parameters => new(
            InstrumentId, BranchName, CourseKind, Students, TeacherRatePerLesson, DiscountPercent, DiscountMonths,
            AlreadyComingPercent, PriceChangePercent, LostStudents, MonthlyAmount, OneTimeCost);
    }

    public record StatusRequest(GrowthIdeaStatus Status);

    public static void MapProfitability(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profitability").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", GetAsync);
        group.MapPost("/ideas/preview", PreviewAsync);
        group.MapPost("/ideas", CreateAsync);
        group.MapPut("/ideas/{id:guid}", UpdateAsync);
        group.MapPost("/ideas/{id:guid}/status", SetStatusAsync);
        group.MapDelete("/ideas/{id:guid}", DeleteAsync);
    }

    private static async Task<IResult> GetAsync(AbderaDbContext db, IClock clock, CancellationToken cancellationToken)
    {
        var snapshot = await ProfitabilitySnapshotBuilder.BuildAsync(db, clock, cancellationToken);
        var ideas = await LoadIdeasAsync(db, cancellationToken);
        return Results.Ok(new OverviewResponse(
            snapshot.Period,
            snapshot.Months.Select(month => new MonthResponse(month.Period, month.Revenue, month.TeacherCost, month.FixedCost, month.Net, month.IsCurrent, month.TeacherCostEstimated)).ToList(),
            ToUnit(snapshot),
            snapshot.Baseline.Instruments
                .Where(row => row.Teachers > 0 || row.Individual + row.Group > 0)
                .Select(row => new InstrumentResponse(row.Id, row.Name, row.Individual, row.Group, row.Teachers, row.TeacherRate,
                    row.BookedHours, row.AvailableHours, row.Utilization is { } u ? decimal.Round(u, 3) : null,
                    row.SpareHours is { } spare ? decimal.Round(spare, 1) : null))
                .ToList(),
            snapshot.Gaps.Select(gap => new GapResponse(gap.Key, gap.Severity, gap.Count, gap.Message)).ToList(),
            ProfitInsights.Build(snapshot),
            ideas.Select(idea => ToResponse(idea, snapshot.Baseline)).ToList()));
    }

    // Formdaki canlı önizleme: kaydetmeden fikrin etkisini hesaplar.
    private static async Task<IResult> PreviewAsync(IdeaRequest request, AbderaDbContext db, IClock clock, CancellationToken cancellationToken)
    {
        // Başlık önizlemede zorunlu değil; yalnızca parametreler doğrulanır.
        EnsureValid(request, title: string.IsNullOrWhiteSpace(request.Title) ? "Önizleme" : request.Title);
        var snapshot = await ProfitabilitySnapshotBuilder.BuildAsync(db, clock, cancellationToken);
        var parameters = GrowthIdea.Normalize(request.Kind, request.Parameters);
        return Results.Ok(ToResponse(GrowthIdeaEvaluator.Evaluate(request.Kind, parameters, snapshot.Baseline)));
    }

    private static async Task<IResult> CreateAsync(IdeaRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock, CancellationToken cancellationToken)
    {
        EnsureValid(request, request.Title);
        await EnsureInstrumentExistsAsync(db, request, cancellationToken);
        var idea = GrowthIdea.Create(request.Kind, request.Title!, request.Note, request.Parameters, AuthContext.GetUserId(principal), clock.UtcNow);
        db.GrowthIdeas.Add(idea);
        await db.SaveChangesAsync(cancellationToken);
        var snapshot = await ProfitabilitySnapshotBuilder.BuildAsync(db, clock, cancellationToken);
        return Results.Created($"/api/profitability/ideas/{idea.Id}", ToResponse(idea, snapshot.Baseline));
    }

    private static async Task<IResult> UpdateAsync(Guid id, IdeaRequest request, AbderaDbContext db, IClock clock, CancellationToken cancellationToken)
    {
        EnsureValid(request, request.Title);
        await EnsureInstrumentExistsAsync(db, request, cancellationToken);
        var idea = await LoadAsync(db, id, cancellationToken);
        idea.Update(request.Kind, request.Title!, request.Note, request.Parameters, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        var snapshot = await ProfitabilitySnapshotBuilder.BuildAsync(db, clock, cancellationToken);
        return Results.Ok(ToResponse(idea, snapshot.Baseline));
    }

    private static async Task<IResult> SetStatusAsync(Guid id, StatusRequest request, AbderaDbContext db, IClock clock, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Status))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["status"] = ["Durum geçersiz."] });
        var idea = await LoadAsync(db, id, cancellationToken);
        idea.SetStatus(request.Status, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, AbderaDbContext db, CancellationToken cancellationToken)
    {
        var idea = await LoadAsync(db, id, cancellationToken);
        db.GrowthIdeas.Remove(idea);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    internal static async Task<List<GrowthIdea>> LoadIdeasAsync(AbderaDbContext db, CancellationToken cancellationToken) =>
        await db.GrowthIdeas.AsNoTracking()
            .OrderBy(idea => idea.Status == GrowthIdeaStatus.Dropped)
            .ThenByDescending(idea => idea.UpdatedAt)
            .ToListAsync(cancellationToken);

    private static async Task<GrowthIdea> LoadAsync(AbderaDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.GrowthIdeas.SingleOrDefaultAsync(idea => idea.Id == id, cancellationToken)
        ?? throw new NotFoundException("Fikir bulunamadı.");

    private static void EnsureValid(IdeaRequest request, string? title)
    {
        var errors = GrowthIdea.Validate(request.Kind, title, request.Note, request.Parameters);
        if (errors.Count > 0) throw new ValidationFailedException(errors);
    }

    private static async Task EnsureInstrumentExistsAsync(AbderaDbContext db, IdeaRequest request, CancellationToken cancellationToken)
    {
        var instrumentId = GrowthIdea.Normalize(request.Kind, request.Parameters).InstrumentId;
        if (instrumentId is { } value && !await db.Instruments.AnyAsync(instrument => instrument.Id == value, cancellationToken))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["instrumentId"] = ["Enstrüman bulunamadı."] });
    }

    private static UnitResponse ToUnit(ProfitSnapshot snapshot)
    {
        var b = snapshot.Baseline;
        var rate = b.AverageTeacherRate ?? 0;
        var lessons = Math.Max(1, b.LessonsPerMonth);
        var individual = b.MarginPerStudent(CourseKind.Individual, rate);
        var group = b.MarginPerStudent(CourseKind.Group, rate);
        return new UnitResponse(
            b.IndividualPrice, b.GroupPrice, b.LessonsPerMonth, b.AverageDiscountRate, b.AverageTeacherRate,
            Round(individual), Round(group), Round(individual / lessons), Round(ProfitInsights.GroupSizeForComparison * group / lessons),
            ProfitInsights.GroupSizeForComparison, b.AverageTenureMonths, b.TenureIsDefault, Round(b.LifetimeValue(rate)),
            b.ActiveEnrollments, snapshot.EndedLast3Months, snapshot.EarlyChurnLast3Months);
    }

    internal static IdeaResponse ToResponse(GrowthIdea idea, ProfitBaseline baseline) => new(
        idea.Id, idea.Kind, idea.Title, idea.Note, idea.Status,
        idea.InstrumentId, idea.BranchName, idea.CourseKind, idea.Students, idea.TeacherRatePerLesson,
        idea.DiscountPercent, idea.DiscountMonths, idea.AlreadyComingPercent, idea.PriceChangePercent, idea.LostStudents,
        idea.MonthlyAmount, idea.OneTimeCost, idea.CreatedAt, idea.UpdatedAt,
        ToResponse(GrowthIdeaEvaluator.Evaluate(idea.Kind, idea.Parameters, baseline)));

    private static EvaluationResponse ToResponse(GrowthIdeaEvaluation evaluation) => new(
        evaluation.MonthlyNet, evaluation.OneTimeCost, evaluation.TwelveMonthNet, evaluation.PaybackMonth,
        evaluation.Series, evaluation.Verdict, evaluation.Reasons);

    private static decimal Round(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}
