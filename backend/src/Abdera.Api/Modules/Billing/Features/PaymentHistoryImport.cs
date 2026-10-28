using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using static Abdera.Api.Shared.SpreadsheetImport;

namespace Abdera.Api.Modules.Billing.Features;

// Excel'den geçmiş ödemeleri içe aktarma (docs/10-decisions.md AF). Yalnızca Admin. Satır = bir
// kurs kaydının ÖDENMİŞ bir ayı: o ayın aidatı (yoksa) açılır ve ödendi işaretlenir.
//
// Para yazan ikinci bir yol açılmaz: her satır Collections.RecordAsync'ten, yani Aidatlar
// ekranındaki tahsilatla aynı çekirdekten geçer - tutarı sunucu tarifeden hesaplar, satırdaki
// tutar yöneticinin elle girdiği "anlaşılan tutar"dır (AgreedTotal, M2): tarifeden azsa fark
// indirim olarak satıra donar, tarifeyi aşamaz. Ödenmiş ay atlanır (mükerrer), birebir derste
// dönemle faturalanan aylar ve tarifesi olmayan aylar satır hatası olur.
public static class PaymentHistoryImport
{
    private const string FirstName = "Öğrenci Adı";
    private const string LastName = "Öğrenci Soyadı";
    private const string Instrument = "Enstrüman";
    private const string Teacher = "Öğretmen";
    private const string Month = "Ay";
    private const string Amount = "Ödenen Tutar";
    private const string PaidOn = "Ödeme Tarihi";
    private const string Method = "Ödeme Şekli";
    private const string Note = "Not";

    public static readonly IReadOnlyList<Column> Columns =
    [
        new(FirstName, true, "Sistemdeki öğrencinin adı.", "Zeynep"),
        new(LastName, true, "Sistemdeki öğrencinin soyadı.", "Kaya"),
        new(Instrument, true, "Öğrencinin kurs kaydındaki enstrüman.", "Piyano"),
        new(Teacher, false, "Öğrenci aynı enstrümanı iki öğretmenden alıyorsa hangisi olduğu.", "Ali Yılmaz"),
        new(Month, true, "Ödenen ay: yıl-ay ya da ay.yıl.", "2026-09"),
        new(Amount, true, "O ay için alınan para. Tarifeden azsa fark indirim olarak yazılır; tarifeyi aşamaz.", "6000"),
        new(PaidOn, true, "Paranın alındığı gün.", "05.09.2026"),
        new(Method, false, "Nakit, Havale, Kart ya da Diğer. Boşsa Diğer.", "Havale"),
        new(Note, false, "Ödemeye not.", ""),
    ];

    public static void MapPaymentHistoryImport(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports/payments").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("/template", () => Template("odeme-gecmisi-sablonu.xlsx", "Ödemeler", Columns));
        group.MapPost("/preview", (IFormFile? file, ClaimsPrincipal principal, AbderaDbContext db, IClock clock) =>
            HandleAsync(file, commit: false, principal, db, clock)).DisableAntiforgery();
        group.MapPost("", (IFormFile? file, ClaimsPrincipal principal, AbderaDbContext db, IClock clock) =>
            HandleAsync(file, commit: true, principal, db, clock)).DisableAntiforgery();
    }

    private static readonly string[] TurkishMonths =
        ["ocak", "şubat", "mart", "nisan", "mayıs", "haziran", "temmuz", "ağustos", "eylül", "ekim", "kasım", "aralık"];

    private static async Task<IResult> HandleAsync(IFormFile? file, bool commit, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var rows = await ReadAsync(file, Columns);
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        var actorId = AuthContext.GetUserId(principal);

        var enrollments = await (
                from enrollment in db.Enrollments.AsNoTracking()
                join student in db.Students on enrollment.StudentId equals student.Id
                join instrument in db.Instruments on enrollment.InstrumentId equals instrument.Id
                join teacher in db.Teachers on enrollment.TeacherId equals teacher.Id
                select new
                {
                    enrollment.Id, enrollment.Status, enrollment.StudentId,
                    student.FirstName, student.LastName, InstrumentName = instrument.Name,
                    TeacherName = teacher.FirstName + " " + teacher.LastName,
                })
            .ToListAsync();
        var paidPeriods = (await db.Receivables.AsNoTracking()
                .Where(receivable => receivable.Status == ReceivableStatus.Paid)
                .Select(receivable => new { receivable.EnrollmentId, receivable.Period })
                .ToListAsync())
            .Select(receivable => (receivable.EnrollmentId, receivable.Period)).ToHashSet();

        var results = new List<RowResult>();
        var seen = new HashSet<(Guid EnrollmentId, string Period)>();
        var imported = 0;
        var importedTotal = 0m;
        await using var transaction = commit ? await db.Database.BeginTransactionAsync() : null;

        foreach (var row in rows)
        {
            var errors = new List<string>();
            var firstName = row.Text(FirstName);
            var lastName = row.Text(LastName);
            var instrumentName = row.Text(Instrument);
            var teacherName = row.Text(Teacher);
            var label = $"{firstName} {lastName}".Trim();
            if (label.Length == 0) label = "(adsız)";
            if (firstName is null || lastName is null) errors.Add("Öğrenci adı ve soyadı zorunlu.");
            if (instrumentName is null) errors.Add($"{Instrument} boş.");

            var period = ParsePeriod(row, out var periodError);
            if (periodError is not null) errors.Add(periodError);
            var amount = row.Amount(Amount, out var amountError);
            if (amountError is not null) errors.Add(amountError);
            else if (amount is null) errors.Add($"{Amount} boş.");
            else if (amount <= 0) errors.Add($"{Amount} 0'dan büyük olmalı.");
            var paidOn = row.Date(PaidOn, out var paidOnError);
            if (paidOnError is not null) errors.Add(paidOnError);
            else if (paidOn is null) errors.Add($"{PaidOn} boş.");
            else if (paidOn > today) errors.Add($"{PaidOn} gelecekte olamaz.");
            var method = ParseMethod(row.Text(Method));
            if (method is null) errors.Add($"{Method} Nakit, Havale, Kart ya da Diğer olmalı.");

            Guid? enrollmentId = null;
            if (errors.Count == 0)
            {
                var candidates = enrollments.Where(e =>
                        Fold(e.FirstName) == Fold(firstName) && Fold(e.LastName) == Fold(lastName)
                        && Fold(e.InstrumentName) == Fold(instrumentName)
                        && (teacherName is null || Fold(e.TeacherName) == Fold(teacherName)))
                    .ToList();
                // Aynı ders için eski (bitmiş) ve yeni kayıt varsa geçmiş ödeme yine tek bir kayda
                // gitmeli; belirsizlikte otomatik seçim yapılmaz (banka eşleştirmesiyle aynı ilke).
                if (candidates.Count == 0)
                    errors.Add($"{label} için {instrumentName} kurs kaydı bulunamadı. Önce öğrenciyi ve kurs kaydını içe aktarın.");
                else if (candidates.Select(c => c.StudentId).Distinct().Count() > 1)
                    errors.Add($"Bu adla birden fazla öğrenci var; bu satırı Aidatlar ekranından elle girin.");
                else if (candidates.Count > 1)
                    errors.Add($"{label} {instrumentName} dersini birden fazla kayıtla alıyor; {Teacher} sütununu doldurun.");
                else
                    enrollmentId = candidates[0].Id;
            }

            if (errors.Count > 0)
            {
                results.Add(new RowResult(row.Number, label, RowStatus.Error, string.Join(" ", errors)));
                continue;
            }

            var rowLabel = $"{label} · {instrumentName} · {period}";
            if (paidPeriods.Contains((enrollmentId!.Value, period!)) || !seen.Add((enrollmentId.Value, period!)))
            {
                results.Add(new RowResult(row.Number, rowLabel, RowStatus.Skipped, "Bu ay zaten ödenmiş; dokunulmadı."));
                continue;
            }

            var request = new Collections.CreateRequest(
                [period!], Discounts: null, paidOn!.Value, method!.Value,
                row.Text(Note) ?? "Excel'den içe aktarıldı", ExpectedTotal: null, AgreedTotal: amount);
            var savepoint = $"row_{row.Number}";
            try
            {
                if (transaction is not null) await transaction.CreateSavepointAsync(savepoint);
                var recorded = await Collections.RecordAsync(enrollmentId.Value, request, null, actorId, db, clock, dryRun: !commit);
                // Önizlemede Total sistemin indirimli hesabıdır; aidat yine ödenen tutarla kaydedilir.
                var detail = recorded.Total == amount ? $"{amount:0.##} {recorded.Currency}"
                    : $"{amount:0.##} {recorded.Currency} · sistemin hesabı {recorded.Total:0.##}; aidat ödenen tutarla kaydedilir";
                if (commit)
                {
                    imported++;
                    importedTotal += amount!.Value;
                }
                results.Add(new RowResult(row.Number, rowLabel, commit ? RowStatus.Imported : RowStatus.Ready, detail));
            }
            catch (Exception ex) when (ex is ValidationFailedException or ConflictException or NotFoundException or ArgumentException)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackToSavepointAsync(savepoint);
                    db.ChangeTracker.Clear();
                }
                results.Add(new RowResult(row.Number, rowLabel, RowStatus.Error, ConflictText(ex)));
            }
        }

        if (transaction is not null)
        {
            if (imported > 0)
            {
                db.AuditLogs.Add(AuditLog.Record(
                    actorId, "import.payments", "Import", Guid.NewGuid(), now,
                    afterJson: JsonSerializer.Serialize(new
                    {
                        fileName = file!.FileName,
                        payments = imported,
                        total = importedTotal,
                        skipped = results.Count(r => r.Status == RowStatus.Skipped),
                        errors = results.Count(r => r.Status == RowStatus.Error),
                    })));
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
        }

        return Results.Ok(Report.From(committed: commit, results));
    }

    // Collections'ın "Seçilen aylardan biri tahsil edilemiyor: 2026-11: ..." öneki tek aylık
    // satırda tekrar olur; yalnızca nedeni bırakılır.
    private static string ConflictText(Exception exception)
    {
        var message = MessageOf(exception);
        const string prefix = "Seçilen aylardan biri tahsil edilemiyor: ";
        if (!message.StartsWith(prefix)) return message;
        var reason = message[prefix.Length..];
        var colon = reason.IndexOf(": ", StringComparison.Ordinal);
        return colon is > 0 and < 8 ? reason[(colon + 2)..] : reason;
    }

    private static string? ParsePeriod(Row row, out string? error)
    {
        error = null;
        var text = row.Text(Month);
        if (text is null)
        {
            error = $"{Month} boş.";
            return null;
        }

        // Excel tarih hücresi ("01.09.2026" olarak biçimlenmiş) Text'te yyyy-MM-dd gelir.
        var match = Regex.Match(text, @"^(\d{4})[-./](\d{1,2})(?:[-./]\d{1,2})?$");
        if (match.Success) return Format(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), out error, text);
        match = Regex.Match(text, @"^(?:\d{1,2}[-./])?(\d{1,2})[-./](\d{4})$");
        if (match.Success) return Format(int.Parse(match.Groups[2].Value), int.Parse(match.Groups[1].Value), out error, text);
        match = Regex.Match(Fold(text), @"^(\p{L}+)\s+(\d{4})$");
        if (match.Success)
        {
            var month = Array.FindIndex(TurkishMonths, name => Fold(name) == match.Groups[1].Value);
            if (month >= 0) return BillingPeriod.Format(int.Parse(match.Groups[2].Value), month + 1);
        }

        error = $"{Month}: '{text}' okunamadı (ör. 2026-09 ya da Eylül 2026).";
        return null;
    }

    private static string? Format(int year, int month, out string? error, string text)
    {
        error = null;
        if (month is >= 1 and <= 12 && year is >= 2000 and <= 2100) return BillingPeriod.Format(year, month);
        error = $"{Month}: '{text}' geçerli bir ay değil.";
        return null;
    }

    private static PaymentMethod? ParseMethod(string? text) => Fold(text) switch
    {
        "" or "diğer" or "diger" => PaymentMethod.Other,
        "nakit" => PaymentMethod.Cash,
        "havale" or "eft" or "havale/eft" => PaymentMethod.Transfer,
        "kart" or "kredi kartı" or "kredi karti" => PaymentMethod.Card,
        _ => null,
    };
}
