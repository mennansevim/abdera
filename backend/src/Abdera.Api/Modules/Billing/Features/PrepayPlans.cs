using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Infrastructure;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// Yıl başı toplu (peşin) ödeme kampanyası - "Toplu ödemelerde indirim uygulanacaktır".
//
// Eski BulkPayments.cs'ten kritik fark: TUTARI ARTIK SUNUCU HESAPLAR. Eskiden istemcinin
// gönderdiği tutarın seçilen ayların İNDİRİMSİZ toplamına birebir eşit olması şart
// koşuluyordu ("Ödeme tutarı bu toplamla aynı olmalı") - kampanya indirimi uygulanmış
// bir ödeme bu kontrolden asla geçemezdi, yani kampanya sisteme hiç girilemiyordu.
//
// Şimdi: ay sayısı -> kademeye göre indirim oranı -> her ayın net tutarı. İstemci yalnızca
// gördüğü toplamı (ExpectedTotal) teyit eder; ekranla sunucu ayrışmışsa işlem durur.
public static class PrepayPlans
{
    public record CreateRequest(
        string StartPeriod,
        int Months,
        DateOnly PaymentDate,
        PaymentMethod Method,
        string? Reference,
        string? Note,
        decimal? ExpectedTotal,
        // Yöneticinin o anda elle girdiği tahsilat toplamı (küsürat/yuvarlama). Boşsa ya da
        // sunucunun hesabıyla aynıysa hesaplanan tutar geçerlidir. ExpectedTotal'ın yerini
        // TUTMAZ: istemci önce sunucunun hesabını gördüğünü teyit eder, sonra onu değiştirir.
        decimal? AgreedTotal = null);

    public record MonthRow(
        string Period, DateOnly DueDate, decimal BaseAmount, decimal Amount,
        bool AlreadyExists, string? BlockedReason);

    public record PreviewResponse(
        Guid EnrollmentId,
        Guid StudentId,
        string StudentName,
        string InstrumentName,
        CourseKind CourseKind,
        string StartPeriod,
        int Months,
        decimal StudentDiscountPercent,
        string? StudentDiscountReason,
        decimal PrepayPercent,
        decimal BaseTotal,
        decimal Total,
        decimal SavingTotal,
        string Currency,
        List<MonthRow> MonthRows,
        List<string> Blockers);

    public record CreateResponse(
        Guid PrepayPlanId,
        string StartPeriod,
        int Months,
        decimal PrepayPercent,
        decimal BaseTotal,
        decimal Total,
        decimal SavingTotal,
        string Currency,
        List<Receivables.ReceivableResponse> Receivables);

    public static void MapPrepayPlans(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/enrollments/{enrollmentId:guid}/prepay-preview", PreviewAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost("/api/enrollments/{enrollmentId:guid}/prepay-plans", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> PreviewAsync(
        Guid enrollmentId, string startPeriod, int months, AbderaDbContext db) =>
        Results.Ok(await BuildPreviewAsync(enrollmentId, startPeriod, months, db));

    private static async Task<IResult> CreateAsync(
        Guid enrollmentId, CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var preview = await BuildPreviewAsync(enrollmentId, request.StartPeriod, request.Months, db);

        if (preview.Blockers.Count > 0)
            throw new ConflictException(
                $"Seçilen aralıkta işlem yapılamayan dönem var: {string.Join(" ", preview.Blockers)}");

        // Ekrandaki toplam ile sunucunun hesabı ayrışmışsa (araya giren bir tarife veya
        // politika değişikliği) yanlış tutarla tahsilat yazmak yerine dururuz.
        if (request.ExpectedTotal is { } expected && expected != preview.Total)
            throw new ConflictException(
                $"Ekrandaki toplam güncel değil. Güncel toplam {preview.Total:0.##} {preview.Currency}. Lütfen ekranı yenileyip tekrar deneyin.");

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var enrollment = await db.Enrollments.SingleAsync(e => e.Id == enrollmentId);
        var pricer = await TuitionPricer.LoadAsync(db);
        var prepayPercent = request.Months > 1 ? pricer.PrepayPercentFor(request.Months) : 0m;
        var periods = BillingPeriod.Sequence(request.StartPeriod, request.Months);

        var existing = await db.Receivables
            .Where(receivable => receivable.EnrollmentId == enrollmentId && periods.Contains(receivable.Period))
            .ToDictionaryAsync(receivable => receivable.Period);

        // Tek aylık ödeme bir "kampanya" değil - plan kimliği yalnızca 2+ ayda üretilir,
        // böylece arayüz gerçekten toplu olan tahsilatı ayırt edebilir.
        var prepayPlanId = request.Months > 1 ? Guid.NewGuid() : (Guid?)null;
        var priced = new List<(string Period, TuitionRate Rate, TuitionCalculator.Breakdown Breakdown)>();
        foreach (var period in periods)
        {
            var (rate, breakdown) = pricer.Price(enrollment, period, prepayPercent)
                ?? throw new ConflictException(pricer.MissingRateMessage(enrollment.CourseKind, period));
            priced.Add((period, rate, breakdown));
        }

        var manuallyAdjusted = request.AgreedTotal is { } agreed && agreed != preview.Total;
        if (manuallyAdjusted)
        {
            IReadOnlyList<TuitionCalculator.Breakdown> adjusted;
            try
            {
                adjusted = TuitionCalculator.AdjustToAgreedTotal(
                    priced.Select(row => row.Breakdown).ToList(), request.AgreedTotal!.Value);
            }
            catch (ArgumentException ex)
            {
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["agreedTotal"] = [ex.Message.Split(" (Parameter")[0]],
                });
            }

            priced = priced.Select((row, index) => (row.Period, row.Rate, adjusted[index])).ToList();
        }

        var targets = new List<Receivable>();
        foreach (var (period, rate, breakdown) in priced)
        {
            if (existing.TryGetValue(period, out var receivable))
            {
                // Ay zaten açılmış ama ödenmemişse kampanya oranıyla yeniden fiyatlanır.
                // Ödeme görmüş bir ay Reprice içinde reddedilir (Blockers zaten yakalar).
                receivable.Reprice(breakdown, prepayPlanId, now);
            }
            else
            {
                receivable = Receivable.Create(
                    enrollment.Id, rate.Id, period, breakdown,
                    rate.Currency, pricer.DueDateFor(period), now, prepayPlanId);
                db.Receivables.Add(receivable);
            }

            targets.Add(receivable);
        }

        // Ödeme ve aidat kayıtları aynı SaveChanges'te oluşur: 10 aylık ödeme yarım kalırsa
        // bazı aylar işaretlenip kalanlar kaybolmaz (eski BulkPayments'tan korunan davranış).
        foreach (var receivable in targets)
        {
            var payment = Payment.Create(
                receivable.Id, receivable.Amount, request.PaymentDate, request.Method,
                request.Reference, request.Note, actorId, now,
                prepayPlanId, request.Months > 1 ? request.Months : null);

            db.Payments.Add(payment);
            receivable.RecordPaymentEffect(receivable.Amount, now);

            db.AuditLogs.Add(AuditLog.Record(
                actorId, "receivable.prepay_payment_recorded", nameof(Receivable), receivable.Id, now,
                afterJson: JsonSerializer.Serialize(new
                {
                    period = receivable.Period,
                    baseAmount = receivable.BaseAmount,
                    discountPercent = receivable.DiscountPercent,
                    discountReason = receivable.DiscountReason,
                    amount = receivable.Amount,
                    months = request.Months,
                    prepayPlanId,
                    computedTotal = preview.Total,
                    agreedTotal = manuallyAdjusted ? request.AgreedTotal : null,
                    newStatus = receivable.Status.ToString(),
                })));
        }

        await db.SaveChangesAsync();

        var ids = targets.Select(receivable => receivable.Id).ToList();
        var totals = await Receivables.ComputeTotalsPaidAsync(ids, db);
        var payments = await Receivables.ComputePaymentsAsync(ids, db);

        var total = targets.Sum(receivable => receivable.Amount);
        return Results.Ok(new CreateResponse(
            prepayPlanId ?? Guid.Empty,
            request.StartPeriod,
            request.Months,
            prepayPercent,
            preview.BaseTotal,
            total,
            preview.BaseTotal - total,
            preview.Currency,
            targets.Select(receivable => Receivables.ToResponse(
                receivable, totals.GetValueOrDefault(receivable.Id), payments.GetValueOrDefault(receivable.Id) ?? []))
                .ToList()));
    }

    private static async Task<PreviewResponse> BuildPreviewAsync(
        Guid enrollmentId, string startPeriod, int months, AbderaDbContext db)
    {
        if (months is < 1 or > 24)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["months"] = ["Ay sayısı 1 ile 24 arasında olmalı."],
            });

        var enrollment = await db.Enrollments.SingleOrDefaultAsync(e => e.Id == enrollmentId)
            ?? throw new NotFoundException("Kurs kaydı bulunamadı.");

        var student = await db.Students.SingleOrDefaultAsync(s => s.Id == enrollment.StudentId);
        var instrument = await db.Instruments.SingleOrDefaultAsync(i => i.Id == enrollment.InstrumentId);

        var pricer = await TuitionPricer.LoadAsync(db);
        var prepayPercent = months > 1 ? pricer.PrepayPercentFor(months) : 0m;
        var periods = BillingPeriod.Sequence(startPeriod, months);

        var existing = await db.Receivables
            .Where(receivable => receivable.EnrollmentId == enrollmentId && periods.Contains(receivable.Period))
            .ToDictionaryAsync(receivable => receivable.Period);

        var studentDiscount = TuitionCalculator.StudentDiscount(pricer.ContextFor(enrollment));
        var rows = new List<MonthRow>();
        var blockers = new List<string>();
        var currency = "TRY";

        foreach (var period in periods)
        {
            var priced = pricer.Price(enrollment, period, prepayPercent);
            if (priced is null)
            {
                blockers.Add(pricer.MissingRateMessage(enrollment.CourseKind, period));
                rows.Add(new MonthRow(period, pricer.DueDateFor(period), 0m, 0m, existing.ContainsKey(period), "Tarife yok"));
                continue;
            }

            currency = priced.Value.Rate.Currency;
            var breakdown = priced.Value.Breakdown;
            string? blocked = null;

            if (existing.TryGetValue(period, out var receivable))
            {
                blocked = receivable.Status switch
                {
                    ReceivableStatus.Paid => "Zaten ödenmiş",
                    ReceivableStatus.Partial => "Kısmi ödeme var",
                    ReceivableStatus.Cancelled => "İptal edilmiş",
                    _ => null,
                };
                if (blocked is not null) blockers.Add($"{period}: {blocked.ToLowerInvariant()}.");
            }

            rows.Add(new MonthRow(
                period, pricer.DueDateFor(period), breakdown.BaseAmount, breakdown.NetAmount,
                existing.ContainsKey(period), blocked));
        }

        var baseTotal = rows.Sum(row => row.BaseAmount);
        var total = rows.Sum(row => row.Amount);

        return new PreviewResponse(
            enrollment.Id,
            enrollment.StudentId,
            student is null ? "Öğrenci" : $"{student.FirstName} {student.LastName}",
            instrument?.Name ?? "Ders",
            enrollment.CourseKind,
            startPeriod,
            months,
            studentDiscount.Percent,
            studentDiscount.Reason,
            prepayPercent,
            baseTotal,
            total,
            baseTotal - total,
            currency,
            rows,
            blockers);
    }
}
