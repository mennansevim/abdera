using System.Text.RegularExpressions;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Ops.Infrastructure;

// Tüm verinin tek .xlsx dökümü (docs/10-decisions.md AJ). İlk iki sayfa içe aktarma şablonuyla
// birebir aynı başlıkları taşır, böylece dosya olduğu gibi Excel'den içe aktar ekranına verilebilir:
// "Öğrenciler" → StudentImport, "Ödemeler" → PaymentHistoryImport (her içe aktarma kendi adındaki
// sayfayı okur). Başlıklar o dosyalardaki Columns listesinden alınır - şablon değişince döküm de
// değişir. Diğer sayfalar başvuru içindir, içe aktarılmaz.
//
// Modüller arası navigation yok: her tablo ayrı okunur, birleştirme bellekte id ile yapılır.
public static class DataExportWorkbook
{
    private const string DateFormat = "dd.MM.yyyy";
    private const string DateTimeFormat = "dd.MM.yyyy HH:mm";
    private const string AmountFormat = "#,##0.00";
    private static readonly Regex MonthlyPeriod = new(@"^\d{4}-\d{2}$");

    public static async Task<byte[]> BuildAsync(AbderaDbContext db, IClock clock, CancellationToken cancellationToken)
    {
        var instruments = await db.Instruments.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Name, cancellationToken);
        var teachers = await db.Teachers.AsNoTracking().ToListAsync(cancellationToken);
        var teacherNames = teachers.ToDictionary(t => t.Id, t => $"{t.FirstName} {t.LastName}");
        var students = await db.Students.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);
        var guardians = await db.Guardians.AsNoTracking().ToDictionaryAsync(g => g.Id, cancellationToken);
        var links = await db.StudentGuardians.AsNoTracking().ToListAsync(cancellationToken);
        var enrollments = await db.Enrollments.AsNoTracking().ToDictionaryAsync(e => e.Id, cancellationToken);
        var receivables = await db.Receivables.AsNoTracking().ToListAsync(cancellationToken);
        var payments = await db.Payments.AsNoTracking().ToListAsync(cancellationToken);
        var paymentsByReceivable = payments.ToLookup(p => p.ReceivableId);

        // PrimaryGuardianResolver ile aynı seçim: birincil işaretli veli, yoksa ilki.
        var primaryLink = links.GroupBy(l => l.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.IsPrimary).ThenBy(l => l.GuardianId).First());

        using var workbook = new XLWorkbook();

        // 1) Öğrenciler - StudentImport şablonu. Satır başına bir kurs kaydı; kaydı olmayan öğrenci tek satır.
        var enrollmentsByStudent = enrollments.Values.ToLookup(e => e.StudentId);
        var studentRows = new List<object?[]>();
        foreach (var student in students.Values.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ThenBy(s => s.BirthDate))
        {
            primaryLink.TryGetValue(student.Id, out var link);
            var guardian = link is null ? null : guardians.GetValueOrDefault(link.GuardianId);
            object?[] Row(Enrollment? enrollment) =>
            [
                student.FirstName, student.LastName, student.BirthDate,
                guardian?.FirstName, guardian?.LastName, guardian?.PhoneNumber, link?.Relationship,
                enrollment is null ? null : instruments.GetValueOrDefault(enrollment.InstrumentId),
                enrollment is null ? null : teacherNames.GetValueOrDefault(enrollment.TeacherId),
                enrollment is null ? null : KindLabel(enrollment.CourseKind),
                enrollment?.StartedAt,
                student.Status == StudentStatus.Active ? "Aktif" : "Pasif",
                student.SiblingDiscount,
                enrollment is null ? null : EnrollmentStatusLabel(enrollment.Status),
                enrollment?.EndedAt,
                enrollment?.ManualDiscountPercent,
            ];

            var courses = enrollmentsByStudent[student.Id].OrderBy(e => e.StartedAt).ToList();
            if (courses.Count == 0) studentRows.Add(Row(null));
            else studentRows.AddRange(courses.Select(Row));
        }
        // Sıra StudentImport.Columns ile aynı; son sütun yalnızca bilgi.
        AddSheet(workbook, StudentImport.SheetName, [.. StudentImport.Columns.Select(c => c.Header), "Elle İndirim %"],
            StudentImport.Columns.Count, studentRows);

        // 2) Ödemeler - PaymentHistoryImport şablonu. Yalnızca tam ödenmiş aylık aidatlar içe
        // aktarılabilir (dönem aidatı D00n ve kısmi ödeme o ekranın kapsamında değil, AF5); hepsi
        // "Aidatlar" ve "Tahsilatlar" sayfalarında.
        var paymentRows = new List<object?[]>();
        foreach (var receivable in receivables
                     .Where(r => r.Status == ReceivableStatus.Paid && r.Amount > 0 && MonthlyPeriod.IsMatch(r.Period))
                     .Where(r => enrollments.ContainsKey(r.EnrollmentId))
                     .OrderBy(r => r.Period))
        {
            var enrollment = enrollments[receivable.EnrollmentId];
            if (!students.TryGetValue(enrollment.StudentId, out var student)) continue;
            var last = paymentsByReceivable[receivable.Id].OrderBy(p => p.PaymentDate).ThenBy(p => p.CreatedAt).LastOrDefault();
            paymentRows.Add(
            [
                student.FirstName, student.LastName,
                instruments.GetValueOrDefault(enrollment.InstrumentId),
                teacherNames.GetValueOrDefault(enrollment.TeacherId),
                receivable.Period, receivable.Amount,
                last?.PaymentDate ?? receivable.DueDate,
                MethodLabel(last?.Method ?? PaymentMethod.Other),
                last?.Note,
            ]);
        }
        AddSheet(workbook, PaymentHistoryImport.SheetName, [.. PaymentHistoryImport.Columns.Select(c => c.Header)], PaymentHistoryImport.Columns.Count, paymentRows);

        // 3) Öğretmenler
        var teacherInstruments = (await db.TeacherInstruments.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(ti => ti.TeacherId);
        var emails = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);
        AddSheet(workbook, "Öğretmenler", ["Adı", "Soyadı", "Enstrümanlar", "E-posta", "Durum", "Kayıt Tarihi"], 0,
            teachers.OrderBy(t => t.LastName).ThenBy(t => t.FirstName).Select(t => new object?[]
            {
                t.FirstName, t.LastName,
                string.Join(", ", teacherInstruments[t.Id].Select(ti => instruments.GetValueOrDefault(ti.InstrumentId)).Where(n => n != null).Order()),
                t.UserId is { } userId ? emails.GetValueOrDefault(userId) : null,
                t.Status == TeacherStatus.Active ? "Aktif" : "Pasif",
                clock.ToSchoolLocal(t.CreatedAt),
            }));

        // 4) Veliler - ikinci veliler de burada (öğrenci şablonu yalnızca birincil veliyi taşır).
        var linksByGuardian = links.ToLookup(l => l.GuardianId);
        AddSheet(workbook, "Veliler", ["Adı", "Soyadı", "Telefon", "Öğrenciler", "Bildirim İzni"], 0,
            guardians.Values.OrderBy(g => g.LastName).ThenBy(g => g.FirstName).Select(g => new object?[]
            {
                g.FirstName, g.LastName, g.PhoneNumber,
                string.Join(", ", linksByGuardian[g.Id]
                    .Select(l => students.TryGetValue(l.StudentId, out var s)
                        ? $"{s.FirstName} {s.LastName}{(l.Relationship is { } r ? $" ({r})" : "")}" : null)
                    .Where(n => n != null)),
                g.NotificationConsent,
            }));

        // 5) Aidatlar - dönem aidatı dahil hepsi.
        AddSheet(workbook, "Aidatlar",
            ["Öğrenci Adı", "Öğrenci Soyadı", "Enstrüman", "Öğretmen", "Ders Türü", "Dönem", "Tahakkuk Tarihi", "Vade",
             "Liste Tutarı", "İndirim %", "İndirim Nedeni", "Tutar", "Ödenen", "Para Birimi", "Durum"], 0,
            receivables.OrderBy(r => r.DueDate).Select(r =>
            {
                enrollments.TryGetValue(r.EnrollmentId, out var e);
                var s = e is null ? null : students.GetValueOrDefault(e.StudentId);
                return new object?[]
                {
                    s?.FirstName, s?.LastName,
                    e is null ? null : instruments.GetValueOrDefault(e.InstrumentId),
                    e is null ? null : teacherNames.GetValueOrDefault(e.TeacherId),
                    e is null ? null : KindLabel(e.CourseKind),
                    r.Period, r.AccruedOn, r.DueDate, r.BaseAmount, r.DiscountPercent, r.DiscountReason, r.Amount,
                    paymentsByReceivable[r.Id].Sum(p => p.Amount), r.Currency, ReceivableStatusLabel(r.Status),
                };
            }));

        // 6) Tahsilatlar - her para girişi ayrı satır.
        var receivableById = receivables.ToDictionary(r => r.Id);
        AddSheet(workbook, "Tahsilatlar",
            ["Ödeme Tarihi", "Öğrenci Adı", "Öğrenci Soyadı", "Enstrüman", "Dönem", "Tutar", "Ödeme Şekli", "Referans", "Not"], 0,
            payments.OrderBy(p => p.PaymentDate).ThenBy(p => p.CreatedAt).Select(p =>
            {
                var r = receivableById.GetValueOrDefault(p.ReceivableId);
                var e = r is null ? null : enrollments.GetValueOrDefault(r.EnrollmentId);
                var s = e is null ? null : students.GetValueOrDefault(e.StudentId);
                return new object?[]
                {
                    p.PaymentDate, s?.FirstName, s?.LastName,
                    e is null ? null : instruments.GetValueOrDefault(e.InstrumentId),
                    r?.Period, p.Amount, MethodLabel(p.Method), p.Reference, p.Note,
                };
            }));

        // 7) Dersler + yoklama
        var attendance = await db.LessonAttendances.AsNoTracking().ToDictionaryAsync(a => a.LessonId, a => a.Status, cancellationToken);
        var lessons = await db.Lessons.AsNoTracking().OrderBy(l => l.StartAt).ToListAsync(cancellationToken);
        AddSheet(workbook, "Dersler", ["Başlangıç", "Bitiş", "Öğrenci Adı", "Öğrenci Soyadı", "Öğretmen", "Enstrüman", "Durum", "Yoklama"], 0,
            lessons.Select(l =>
            {
                var s = students.GetValueOrDefault(l.StudentId);
                return new object?[]
                {
                    clock.ToSchoolLocal(l.StartAt), clock.ToSchoolLocal(l.EndAt), s?.FirstName, s?.LastName,
                    teacherNames.GetValueOrDefault(l.TeacherId), instruments.GetValueOrDefault(l.InstrumentId),
                    LessonStatusLabel(l.Status),
                    attendance.TryGetValue(l.Id, out var a) ? AttendanceLabel(a) : null,
                };
            }));

        // 8) Giderler
        var expenses = await db.Expenses.AsNoTracking().OrderBy(e => e.ExpenseDate).ToListAsync(cancellationToken);
        AddSheet(workbook, "Giderler", ["Tarih", "Kategori", "Açıklama", "Tutar", "Para Birimi", "Not"], 0,
            expenses.Select(e => new object?[] { e.ExpenseDate, ExpenseLabel(e.Category), e.Description, e.Amount, e.Currency, e.Note }));

        AddGuide(workbook, clock.ToSchoolLocal(clock.UtcNow));

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    // importColumns: içe aktarmanın okuduğu ilk sütun sayısı; o başlıklar şablondaki renkle,
    // yalnızca bilgi amaçlı ek sütunlar gri boyanır.
    private static void AddSheet(XLWorkbook workbook, string name, IReadOnlyList<string> headers, int importColumns, IEnumerable<object?[]> rows)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var column = 0; column < headers.Count; column++)
        {
            var cell = sheet.Cell(1, column + 1);
            cell.Value = headers[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = column < importColumns ? XLColor.FromHtml("#FDE7C8") : XLColor.FromHtml("#EEF1F5");
            sheet.Column(column + 1).Width = Math.Max(14, headers[column].Length + 4);
        }

        var rowNumber = 2;
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
                Write(sheet.Cell(rowNumber, column + 1), row[column]);
            rowNumber++;
        }
        sheet.SheetView.FreezeRows(1);
    }

    private static void Write(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case DateOnly date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = DateFormat;
                break;
            case DateTimeOffset local:
                cell.Value = local.DateTime;
                cell.Style.DateFormat.Format = DateTimeFormat;
                break;
            case decimal amount:
                cell.Value = amount;
                cell.Style.NumberFormat.Format = AmountFormat;
                break;
            case bool flag:
                cell.Value = flag ? "Evet" : "Hayır";
                break;
            default:
                // Metin olarak yazılır: "2026-09" ya da "+905321234567" Excel'de sayıya/tarihe dönmesin.
                cell.Value = value.ToString();
                break;
        }
    }

    private static void AddGuide(XLWorkbook workbook, DateTimeOffset createdAt)
    {
        var sheet = workbook.Worksheets.Add("Bu dosya hakkında");
        string[] lines =
        [
            $"Abdera veri dökümü · {createdAt:dd.MM.yyyy HH:mm}",
            "",
            "Öğrenciler ve Ödemeler sayfaları Excel'den içe aktar ekranındaki şablonlarla aynıdır; dosyayı olduğu gibi oraya verebilirsin.",
            "Önce Öğrenci + veli, sonra Ödeme geçmişi aktarılır. Öğretmenlerin hedef sistemde aynı adla kayıtlı olması gerekir.",
            "Turuncu başlıklı sütunlar içe aktarılır (öğrenci/kurs durumu ve kardeş indirimi dahil; bitmiş kursa aidat açılmaz). Gri başlıklı sütunlar yalnızca bilgi içindir.",
            "Sistemde zaten olan öğrenci ve ödenmiş ay atlanır; aynı dosyayı iki kez vermek kayıt çoğaltmaz.",
            "Ödemeler sayfasında yalnızca tam ödenmiş aylık aidatlar var. Dönem aidatları (D001...) ve kısmi ödemeler Aidatlar ve Tahsilatlar sayfalarındadır.",
            "Öğretmenler, Veliler, Aidatlar, Tahsilatlar, Dersler ve Giderler sayfaları başvuru içindir.",
        ];
        for (var index = 0; index < lines.Length; index++) sheet.Cell(index + 1, 1).Value = lines[index];
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Column(1).Width = 120;
    }

    private static string KindLabel(CourseKind kind) => kind == CourseKind.Group ? "Grup" : "Birebir";

    private static string EnrollmentStatusLabel(EnrollmentStatus status) => status switch
    {
        EnrollmentStatus.Active => "Aktif",
        EnrollmentStatus.Paused => "Durduruldu",
        _ => "Bitti",
    };

    // PaymentHistoryImport.ParseMethod'un okuduğu kelimeler.
    private static string MethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Nakit",
        PaymentMethod.Transfer => "Havale",
        PaymentMethod.Card => "Kart",
        _ => "Diğer",
    };

    private static string ReceivableStatusLabel(ReceivableStatus status) => status switch
    {
        ReceivableStatus.Paid => "Ödendi",
        ReceivableStatus.Partial => "Kısmi",
        ReceivableStatus.Overdue => "Gecikti",
        ReceivableStatus.Cancelled => "İptal",
        _ => "Ödenmedi",
    };

    private static string LessonStatusLabel(LessonStatus status) => status switch
    {
        LessonStatus.Rescheduled => "Ertelendi",
        LessonStatus.Cancelled => "İptal",
        LessonStatus.Completed => "Tamamlandı",
        LessonStatus.Makeup => "Telafi",
        _ => "Planlandı",
    };

    private static string AttendanceLabel(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => "Geldi",
        AttendanceStatus.Absent => "Gelmedi",
        _ => "Mazeretli",
    };

    private static string ExpenseLabel(ExpenseCategory category) => category switch
    {
        ExpenseCategory.Salary => "Maaş",
        ExpenseCategory.Utilities => "Elektrik / su",
        ExpenseCategory.Rent => "Kira",
        ExpenseCategory.Marketing => "Reklam",
        ExpenseCategory.Maintenance => "Bakım / akort",
        ExpenseCategory.TaxAndInsurance => "Vergi / SGK",
        ExpenseCategory.Supplies => "Malzeme / kırtasiye",
        ExpenseCategory.Equipment => "Enstrüman / ekipman",
        _ => "Diğer",
    };
}
