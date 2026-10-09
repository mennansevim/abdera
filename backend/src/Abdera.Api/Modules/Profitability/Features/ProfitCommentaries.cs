using Abdera.Api.Modules.Profitability.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Abdera.Api.Modules.Profitability.Features;

// Kârlılık ekranının aylık yapay zekâ yorumu (docs/10-decisions.md V3).
//
// Tembel üretim, StudentProgressSummary ile aynı desen: ekran o ay ilk açıldığında yorum bir
// kez üretilir ve profit_commentaries'e yazılır; ay boyunca kayıttan gösterilir. Yönetici
// veriyi düzelttikten sonra (ör. öğretmen ücretlerini girdi) ayda en fazla
// ProfitCommentary.MaxRefreshesPerMonth kez yeniden ürettirebilir. Böylece token tüketimi
// ayda 1-4 çağrıyla sınırlı kalır.
//
// Sağlayıcı kapalıysa ya da hata verirse ekranın geri kalanı etkilenmez; yorum kartı
// "kapalı/üretilemedi" der, uydurma metin gösterilmez.
public static class ProfitCommentaries
{
    public enum CommentaryStatus
    {
        Ready,
        Unavailable,
        Failed,
    }

    public record Response(
        CommentaryStatus Status, string Period, string? Text, DateTimeOffset? GeneratedAt,
        int RefreshesLeft, DateOnly NextAutomaticOn);

    public static void MapProfitCommentaries(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profitability/commentary").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", GetAsync);
        group.MapPost("/refresh", RefreshAsync);
    }

    private static async Task<IResult> GetAsync(
        AbderaDbContext db, IClock clock, IProfitCommentaryGenerator generator, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var period = CurrentPeriod(clock);
        var cached = await db.ProfitCommentaries.AsNoTracking().SingleOrDefaultAsync(row => row.Period == period, cancellationToken);
        if (cached is not null) return Results.Ok(ToResponse(cached, clock));
        if (!generator.IsAvailable) return Results.Ok(Empty(CommentaryStatus.Unavailable, period, clock));

        var text = await GenerateAsync(db, clock, generator, loggerFactory, cancellationToken);
        if (text is null) return Results.Ok(Empty(CommentaryStatus.Failed, period, clock));

        var commentary = ProfitCommentary.Create(period, text, generator.ModelName, clock.UtcNow);
        db.ProfitCommentaries.Add(commentary);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // İki sekme aynı anda ayın ilk yorumunu üretti; önce yazılan geçerli.
            db.ChangeTracker.Clear();
            var winner = await db.ProfitCommentaries.AsNoTracking().SingleAsync(row => row.Period == period, cancellationToken);
            return Results.Ok(ToResponse(winner, clock));
        }
        return Results.Ok(ToResponse(commentary, clock));
    }

    private static async Task<IResult> RefreshAsync(
        AbderaDbContext db, IClock clock, IProfitCommentaryGenerator generator, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (!generator.IsAvailable)
            throw new ConflictException("Yapay zekâ yorumu kapalı: okul için bir AI sağlayıcısı yapılandırılmamış.");

        var period = CurrentPeriod(clock);
        var cached = await db.ProfitCommentaries.SingleOrDefaultAsync(row => row.Period == period, cancellationToken);
        // Hak dolmuşsa sağlayıcıya hiç gitme: limit tokeni korumak için var.
        if (cached is { CanRefresh: false })
            throw new ConflictException($"Bu ayın yorumu en fazla {ProfitCommentary.MaxRefreshesPerMonth} kez yenilenebilir; hak doldu.");

        var text = await GenerateAsync(db, clock, generator, loggerFactory, cancellationToken)
            ?? throw new ConflictException("Yorum şu an üretilemedi; biraz sonra yeniden dene.");

        if (cached is null)
        {
            cached = ProfitCommentary.Create(period, text, generator.ModelName, clock.UtcNow);
            db.ProfitCommentaries.Add(cached);
        }
        else
        {
            cached.Refresh(text, generator.ModelName, clock.UtcNow);
        }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(cached, clock));
    }

    private static async Task<string?> GenerateAsync(
        AbderaDbContext db, IClock clock, IProfitCommentaryGenerator generator, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var snapshot = await ProfitabilitySnapshotBuilder.BuildAsync(db, clock, cancellationToken);
        var ideas = await Profitability.LoadIdeasAsync(db, cancellationToken);
        var facts = ProfitCommentaryFacts.Build(
            snapshot,
            ProfitInsights.Build(snapshot),
            ideas.Select(idea => new ProfitCommentaryFacts.IdeaFact(
                idea.Title, idea.Kind, idea.Status, GrowthIdeaEvaluator.Evaluate(idea.Kind, idea.Parameters, snapshot.Baseline))).ToList());

        var result = await generator.GenerateAsync(facts, cancellationToken);
        if (result.Success && !string.IsNullOrWhiteSpace(result.Text)) return result.Text;

        loggerFactory.CreateLogger(typeof(ProfitCommentaries)).LogWarning("Kârlılık yorumu üretilemedi: {Error}", result.Error);
        return null;
    }

    private static string CurrentPeriod(IClock clock)
    {
        var local = clock.ToSchoolLocal(clock.UtcNow);
        return Billing.Domain.BillingPeriod.Format(local.Year, local.Month);
    }

    private static DateOnly NextMonth(IClock clock)
    {
        var local = clock.ToSchoolLocal(clock.UtcNow);
        return new DateOnly(local.Year, local.Month, 1).AddMonths(1);
    }

    private static Response ToResponse(ProfitCommentary commentary, IClock clock) => new(
        CommentaryStatus.Ready, commentary.Period, commentary.Text, commentary.UpdatedAt,
        ProfitCommentary.MaxRefreshesPerMonth - commentary.RefreshCount, NextMonth(clock));

    private static Response Empty(CommentaryStatus status, string period, IClock clock) =>
        new(status, period, null, null, status == CommentaryStatus.Unavailable ? 0 : ProfitCommentary.MaxRefreshesPerMonth, NextMonth(clock));
}
