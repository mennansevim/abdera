using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Infrastructure;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Abdera.Api.Modules.Billing.Features;

// Aidatlar ekranının tek tahsilat yolu (docs/10-decisions.md H17): bir kurs kaydının SEÇİLEN
// aylarını, yöneticinin o anda açıp kapattığı indirimlerle tek işlemde tahsil eder. Tek ay ile
// birkaç ayın peşin tahsilatı arasında ayrı akış yok - kaç ay seçildiyse o kadar.
//
// Kurallar PrepayPlans ile aynı: tutarı SUNUCU hesaplar (TuitionCalculator), istemci yalnızca
// gördüğü toplamı (ExpectedTotal) teyit eder; ödeme görmüş bir ay yeniden fiyatlanmaz.
// Farkı: aylar ardışık olmak zorunda değil (gecikmiş Eylül + bu ay + ileri iki ay) ve indirimler
// otomatik çıkarım yerine açık seçim. Seçim boş gelirse (Discounts = null, listedeki tek dokunuş
// "Ödendi") açılmış aylar donmuş tutarıyla olduğu gibi tahsil edilir.
public static class Collections
{
    // Yöneticinin ödeme anındaki indirim seçimi. Kardeş/çoklu kurs öğrencinin olgusundan
    // bağımsız açılıp kapatılabilir ("tüm indirimleri opsiyonel yapabileyim"); Manual doluysa
    // otomatik ikisinin yerine geçer, Prepay seçilen ay sayısının kademesine göre üstüne biner.
    public record DiscountChoice(bool Sibling, bool MultiCourse, bool Prepay, decimal? ManualPercent);

    public record CreateRequest(
        List<string> Periods,
        DiscountChoice? Discounts,
        DateOnly PaymentDate,
        PaymentMethod Method,
        string? Note,
        decimal? ExpectedTotal,
        // Küsürat/yuvarlama (M2) - PrepayPlans.CreateRequest.AgreedTotal ile aynı anlam: aylar
        // ödendi sayılır, fark indirim olarak satıra yazılır.
        decimal? AgreedTotal = null,
        // Kısmi ödeme: velinin verdiği tutar ayın kalanından az ve kalan BORÇ OLARAK KALIR (ay
        // Partial olur). Yalnızca tek ayda; AgreedTotal ile birlikte gönderilemez.
        decimal? PartialAmount = null);

    public record Defaults(
        bool Sibling, bool MultiCourse, decimal? ManualPercent, string? ManualReason,
        decimal SiblingPercent, decimal MultiCoursePercent);

    public record Row(
        string Period, Guid? ReceivableId, ReceivableStatus? Status,
        decimal BaseAmount, decimal DiscountPercent, string? DiscountReason, decimal Amount,
        decimal AlreadyPaid, decimal Due, bool Repriced, string? BlockedReason);

    public record Quote(
        Guid EnrollmentId, string StudentName, string InstrumentName, CourseKind CourseKind, string Currency,
        Defaults Defaults, decimal PrepayPercent, List<Row> Rows, decimal BaseTotal, decimal Total, List<string> Blockers);

    public record CreateResponse(decimal Total, string Currency, Guid? PrepayPlanId, List<Receivables.ReceivableResponse> Receivables, bool Replayed = false);

    public static void MapCollections(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/enrollments/{enrollmentId:guid}/collection-preview", PreviewAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost("/api/enrollments/{enrollmentId:guid}/collections", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    // GET: ?periods=2026-09&periods=2026-10&sibling=true&multiCourse=false&prepay=true&manualPercent=
    // İndirim parametreleri hiç verilmezse öğrencinin olgularından gelen varsayılanlar kullanılır.
    private static async Task<IResult> PreviewAsync(
        Guid enrollmentId, string[]? periods, bool? sibling, bool? multiCourse, bool? prepay, decimal? manualPercent,
        AbderaDbContext db)
    {
        var context = await LoadAsync(enrollmentId, db);
        var choice = new DiscountChoice(
            sibling ?? context.Defaults.Sibling,
            multiCourse ?? context.Defaults.MultiCourse,
            prepay ?? true,
            sibling is null && multiCourse is null && manualPercent is null ? context.Defaults.ManualPercent : manualPercent);
        var quote = await BuildQuoteAsync(context, periods ?? [], choice, db);
        return Results.Ok(quote.Quote);
    }

    private static async Task<IResult> CreateAsync(
        Guid enrollmentId, CreateRequest request, HttpRequest httpRequest,
        ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var idempotencyKey = httpRequest.Headers["Idempotency-Key"].ToString().Trim();
        if (idempotencyKey.Length is < 8 or > 100)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["Tahsilat isteği 8-100 karakterlik bir Idempotency-Key başlığı taşımalı."],
            });

        // Aynı istek (çift dokunma, ağ kesintisi) ikinci bir mali kayıt üretmez. Anahtar
        // tahsilatın ilk ödeme satırında saklanır.
        var replay = await db.Payments.AsNoTracking().SingleOrDefaultAsync(payment => payment.IdempotencyKey == idempotencyKey);
        if (replay is not null) return Results.Ok(new CreateResponse(replay.Amount, "TRY", replay.PrepayPlanId, [], Replayed: true));

        var context = await LoadAsync(enrollmentId, db);
        var built = await BuildQuoteAsync(context, request.Periods ?? [], request.Discounts, db);
        var quote = built.Quote;

        if (quote.Rows.Count == 0)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["periods"] = ["En az bir ay seçin."],
            });
        if (quote.Blockers.Count > 0)
            throw new ConflictException($"Seçilen aylardan biri tahsil edilemiyor: {string.Join(" ", quote.Blockers)}");
        if (request.ExpectedTotal is { } expected && expected != quote.Total)
            throw new ConflictException(
                $"Ekrandaki toplam güncel değil. Güncel toplam {quote.Total:0.##} {quote.Currency}. Ekranı yenileyip tekrar deneyin.");

        var priced = built.Priced;
        if (request.PartialAmount is { } partial)
        {
            var dueNow = quote.Total;
            string? problem = priced.Count != 1 ? "Kısmi ödeme yalnızca tek bir ay seçiliyken alınabilir."
                : request.AgreedTotal is not null ? "Kısmi ödeme ile tutar düzeltmesi birlikte yapılamaz."
                : partial <= 0 || partial >= dueNow ? $"Kısmi tutar 0'dan büyük ve ayın kalanından ({dueNow:0.##}) küçük olmalı."
                : null;
            if (problem is not null)
                throw new ValidationFailedException(new Dictionary<string, string[]> { ["partialAmount"] = [problem] });
        }

        var manuallyAdjusted = request.AgreedTotal is { } agreed && agreed != quote.Total;
        if (manuallyAdjusted)
        {
            if (priced.Any(item => !item.CanReprice))
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["agreedTotal"] = ["Kısmi ödemesi olan bir ay seçiliyken tutar elle değiştirilemez."],
                });
            try
            {
                var adjusted = TuitionCalculator.AdjustToAgreedTotal(
                    priced.Select(item => item.Breakdown).ToList(), request.AgreedTotal!.Value);
                priced = priced.Select((item, index) => item with { Breakdown = adjusted[index] }).ToList();
            }
            catch (ArgumentException ex)
            {
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["agreedTotal"] = [ex.Message.Split(" (Parameter")[0]],
                });
            }
        }

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var months = priced.Count;
        // Tek ay bir "toplu ödeme" değil; plan kimliği yalnızca 2+ ayda üretilir (PrepayPlans ile aynı).
        var prepayPlanId = months > 1 ? Guid.NewGuid() : (Guid?)null;
        var policy = TuitionCalculator.StudentDiscount(context.Pricer.ContextFor(context.Enrollment));

        var settled = new List<Receivable>();
        var first = true;
        foreach (var item in priced)
        {
            var receivable = item.Existing;
            decimal alreadyPaid = item.AlreadyPaid;
            if (receivable is null)
            {
                receivable = Receivable.Create(
                    context.Enrollment.Id, item.Rate!.Id, item.Period, item.Breakdown,
                    item.Rate.Currency, context.Pricer.DueDateFor(item.Period), now, prepayPlanId);
                db.Receivables.Add(receivable);
            }
            else if (item.CanReprice && (item.Breakdown != Snapshot(receivable) || prepayPlanId is not null))
            {
                receivable.Reprice(item.Breakdown, prepayPlanId, now);
            }

            var due = request.PartialAmount ?? receivable.Amount - alreadyPaid;
            if (due <= 0) continue;

            db.Payments.Add(Payment.Create(
                receivable.Id, due, request.PaymentDate, request.Method, null, request.Note, actorId, now,
                prepayPlanId, prepayPlanId is null ? null : months,
                idempotencyKey: first ? idempotencyKey : null));
            first = false;
            receivable.RecordPaymentEffect(alreadyPaid + due, now);
            settled.Add(receivable);

            // Seçilen indirim, politikanın o öğrenci için ne diyeceğiyle yan yana yazılır:
            // "bu ayda kardeş indirimi neden yok" sorusu tek satırdan yanıtlanabilmeli.
            db.AuditLogs.Add(AuditLog.Record(
                actorId, "receivable.collection_recorded", nameof(Receivable), receivable.Id, now,
                afterJson: JsonSerializer.Serialize(new
                {
                    period = receivable.Period,
                    baseAmount = receivable.BaseAmount,
                    discountPercent = receivable.DiscountPercent,
                    discountReason = receivable.DiscountReason,
                    amount = receivable.Amount,
                    paid = due,
                    method = request.Method.ToString(),
                    months,
                    prepayPlanId,
                    chosenDiscounts = request.Discounts,
                    policyDiscountPercent = policy.Percent,
                    policyDiscountReason = policy.Reason,
                    computedTotal = quote.Total,
                    agreedTotal = manuallyAdjusted ? request.AgreedTotal : null,
                    newStatus = receivable.Status.ToString(),
                })));
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı ay başka bir sekmeden/eşzamanlı istekle açıldı ya da aynı anahtar yarıştı.
            throw new ConflictException("Bu aylar az önce başka bir işlemle değişti. Ekranı yenileyip tekrar deneyin.");
        }

        var ids = settled.Select(receivable => receivable.Id).ToList();
        var totals = await Receivables.ComputeTotalsPaidAsync(ids, db);
        var payments = await Receivables.ComputePaymentsAsync(ids, db);
        return Results.Ok(new CreateResponse(
            request.PartialAmount ?? settled.Sum(receivable => receivable.Amount) - priced.Sum(item => item.AlreadyPaid),
            quote.Currency,
            prepayPlanId,
            settled.Select(receivable => Receivables.ToResponse(
                receivable, totals.GetValueOrDefault(receivable.Id), payments.GetValueOrDefault(receivable.Id) ?? [])).ToList()));
    }

    private sealed record LoadedContext(Enrollment Enrollment, string StudentName, string InstrumentName, TuitionPricer Pricer, Defaults Defaults);

    private sealed record Priced(
        string Period, Receivable? Existing, TuitionRate? Rate, TuitionCalculator.Breakdown Breakdown,
        decimal AlreadyPaid, bool CanReprice);

    private static async Task<LoadedContext> LoadAsync(Guid enrollmentId, AbderaDbContext db)
    {
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(e => e.Id == enrollmentId)
            ?? throw new NotFoundException("Kurs kaydı bulunamadı.");
        var student = await db.Students.AsNoTracking().SingleOrDefaultAsync(s => s.Id == enrollment.StudentId);
        var instrument = await db.Instruments.AsNoTracking().SingleOrDefaultAsync(i => i.Id == enrollment.InstrumentId);
        var pricer = await TuitionPricer.LoadAsync(db);
        var facts = pricer.ContextFor(enrollment);
        return new LoadedContext(
            enrollment,
            student is null ? "Öğrenci" : $"{student.FirstName} {student.LastName}",
            instrument?.Name ?? "Ders",
            pricer,
            new Defaults(
                facts.HasSibling, facts.AttendsMultipleCourses, enrollment.ManualDiscountPercent, enrollment.ManualDiscountReason,
                pricer.Settings.SiblingDiscountPercent, pricer.Settings.MultiCourseDiscountPercent));
    }

    private static async Task<(Quote Quote, List<Priced> Priced)> BuildQuoteAsync(
        LoadedContext context, IEnumerable<string> requestedPeriods, DiscountChoice? choice, AbderaDbContext db)
    {
        var periods = requestedPeriods.Select(period => period?.Trim() ?? "").Distinct().ToList();
        foreach (var period in periods) BillingPeriod.Parse(period, "periods");
        periods.Sort(StringComparer.Ordinal);
        if (periods.Count > 24)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["periods"] = ["Tek seferde en fazla 24 ay tahsil edilebilir."],
            });

        var enrollment = context.Enrollment;
        var pricer = context.Pricer;
        var existing = await db.Receivables
            .Where(receivable => receivable.EnrollmentId == enrollment.Id && periods.Contains(receivable.Period))
            .ToDictionaryAsync(receivable => receivable.Period);
        var paid = await Receivables.ComputeTotalsPaidAsync(existing.Values.Select(receivable => receivable.Id), db);

        // Peşin kademesi yeniden fiyatlanabilecek aylar üzerinden sayılır - kısmi ödenmiş ay
        // tutarı değişmeyeceği için "peşin" sayılmaz.
        var repriceable = periods.Count(period =>
            !existing.TryGetValue(period, out var receivable) || CanReprice(receivable, paid.GetValueOrDefault(receivable.Id)));
        var prepayPercent = choice is { Prepay: true } && repriceable > 1 ? pricer.PrepayPercentFor(repriceable) : 0m;

        var discountContext = choice is null
            ? pricer.ContextFor(enrollment)
            : new TuitionCalculator.DiscountContext(
                AttendsMultipleCourses: choice.MultiCourse,
                HasSibling: choice.Sibling,
                ManualPercent: choice.ManualPercent is > 0 ? Math.Min(choice.ManualPercent.Value, 100m) : null,
                ManualReason: choice.ManualPercent == enrollment.ManualDiscountPercent ? enrollment.ManualDiscountReason : null,
                MultiCoursePercent: pricer.Settings.MultiCourseDiscountPercent,
                SiblingPercent: pricer.Settings.SiblingDiscountPercent);

        var rows = new List<Row>();
        var priced = new List<Priced>();
        var blockers = new List<string>();
        var currency = "TRY";

        foreach (var period in periods)
        {
            existing.TryGetValue(period, out var receivable);
            var alreadyPaid = receivable is null ? 0m : paid.GetValueOrDefault(receivable.Id);

            string? blocked = receivable?.Status switch
            {
                ReceivableStatus.Paid => "Zaten ödenmiş",
                ReceivableStatus.Cancelled => "İptal edilmiş",
                _ => null,
            };
            if (blocked is not null)
            {
                blockers.Add($"{period}: {blocked.ToLowerInvariant()}.");
                rows.Add(new Row(period, receivable!.Id, receivable.Status, receivable.BaseAmount, receivable.DiscountPercent,
                    receivable.DiscountReason, receivable.Amount, alreadyPaid, 0m, false, blocked));
                continue;
            }

            // Üzerinde ödeme olan ay (Partial ya da kısmi ödenip vadesi geçmiş Overdue) ve "seçimsiz"
            // (tek dokunuş) tahsilattaki açılmış ay donmuş tutarıyla kalır, kalanı tahsil edilir.
            if (receivable is not null && (!CanReprice(receivable, alreadyPaid) || choice is null))
            {
                currency = receivable.Currency;
                var snapshot = Snapshot(receivable);
                priced.Add(new Priced(period, receivable, null, snapshot, alreadyPaid, CanReprice(receivable, alreadyPaid)));
                rows.Add(new Row(period, receivable.Id, receivable.Status, receivable.BaseAmount, receivable.DiscountPercent,
                    receivable.DiscountReason, receivable.Amount, alreadyPaid, receivable.Amount - alreadyPaid, false, null));
                continue;
            }

            var rate = pricer.RateFor(enrollment.CourseKind, BillingPeriod.FirstDay(period));
            if (rate is null)
            {
                var message = pricer.MissingRateMessage(enrollment.CourseKind, period);
                blockers.Add(message);
                rows.Add(new Row(period, receivable?.Id, receivable?.Status, 0m, 0m, null, 0m, alreadyPaid, 0m, false, "Tarife yok"));
                continue;
            }

            currency = rate.Currency;
            var breakdown = prepayPercent > 0
                ? TuitionCalculator.ComputePrepaidMonthly(rate.MonthlyAmount, discountContext, prepayPercent)
                : TuitionCalculator.ComputeMonthly(rate.MonthlyAmount, discountContext);
            priced.Add(new Priced(period, receivable, rate, breakdown, alreadyPaid, true));
            rows.Add(new Row(period, receivable?.Id, receivable?.Status, breakdown.BaseAmount, breakdown.DiscountPercent,
                breakdown.DiscountReason, breakdown.NetAmount, alreadyPaid, breakdown.NetAmount - alreadyPaid,
                receivable is not null && breakdown != Snapshot(receivable), null));
        }

        var quote = new Quote(
            enrollment.Id, context.StudentName, context.InstrumentName, enrollment.CourseKind, currency,
            context.Defaults, prepayPercent, rows,
            rows.Where(row => row.BlockedReason is null).Sum(row => row.BaseAmount),
            rows.Sum(row => row.Due),
            blockers);
        return (quote, priced);
    }

    // Receivable.Reprice yalnızca Partial'ı tanır; vadesi geçince Overdue'ya dönen kısmi ödenmiş
    // bir ay da ödeme taşıdığı için burada ödenen tutara ayrıca bakılır.
    private static bool CanReprice(Receivable receivable, decimal alreadyPaid) =>
        alreadyPaid == 0m && receivable.Status is ReceivableStatus.Unpaid or ReceivableStatus.Overdue;

    private static TuitionCalculator.Breakdown Snapshot(Receivable receivable) =>
        new(receivable.BaseAmount, receivable.DiscountPercent, receivable.DiscountReason, receivable.Amount);
}
