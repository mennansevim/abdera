using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Shared;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Excel'den içe aktarma (docs/10-decisions.md AF): önizleme hiçbir şey yazmaz, içe aktarma
// yalnızca hazır satırları yazar; mükerrer satır atlanır, hatalı satır raporlanır.
public class SpreadsheetImportFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public SpreadsheetImportFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Beklenmeyen durum: {(int)response.StatusCode} {response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private static MultipartFormDataContent Workbook(string[] headers, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sayfa1");
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        for (var row = 0; row < rows.Length; row++)
        for (var column = 0; column < rows[row].Length; column++)
        {
            sheet.Cell(row + 2, column + 1).Value = rows[row][column] switch
            {
                null => Blank.Value,
                DateTime date => date,
                decimal amount => amount,
                var value => value.ToString(),
            };
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var file = new ByteArrayContent(stream.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new MultipartFormDataContent { { file, "file", "aktarim.xlsx" } };
    }

    private static readonly string[] StudentHeaders =
        ["Öğrenci Adı", "Öğrenci Soyadı", "Doğum Tarihi", "Veli Adı", "Veli Soyadı", "Veli Telefonu", "Yakınlık", "Enstrüman", "Öğretmen", "Ders Türü", "Başlangıç Tarihi"];

    private static readonly string[] PaymentHeaders =
        ["Öğrenci Adı", "Öğrenci Soyadı", "Enstrüman", "Ay", "Ödenen Tutar", "Ödeme Tarihi", "Ödeme Şekli"];

    private static async Task<(string Piano, string Guitar)> CreateTeacherAsync(HttpClient admin, string firstName, string lastName)
    {
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(instrument => instrument.Code == "PIANO");
        var guitar = instruments.Single(instrument => instrument.Code == "GUITAR");
        await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/teachers", new Teachers.CreateRequest(firstName, lastName, [piano.Id, guitar.Id], null)));
        return (piano.Name, guitar.Name);
    }

    [Fact]
    public async Task Students_with_guardians_and_courses_are_imported_after_a_preview_that_writes_nothing()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await AdminAsync();
        var (piano, guitar) = await CreateTeacherAsync(admin, "Aktarim", "Hocasi");

        // Sistemde zaten olan öğrenci ve kardeşin velisi.
        var existing = await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync(
            "/api/students", new Students.CreateRequest("Mevcut", "Ogrenci", new DateOnly(2013, 5, 5))));
        var knownGuardian = await ReadAsync<Guardians.GuardianResponse>(await admin.PostAsJsonAsync(
            "/api/guardians", new Guardians.CreateRequest("Kayitli", "Veli", "0532 700 00 01")));
        (await admin.PostAsJsonAsync($"/api/students/{existing.Id}/guardians",
            new LinkGuardianToStudent.Request(knownGuardian.Id, "Anne", true))).EnsureSuccessStatusCode();

        var start = new DateTime(2099, 1, 1);
        using var content = Workbook(StudentHeaders,
            // İki enstrüman: aynı öğrenci iki satırda, bir kez açılır.
            ["Deniz", "Aktarım", new DateTime(2015, 3, 12), "Selin", "Aktarım", "0532 700 00 02", "Anne", piano, "Aktarim Hocasi", "Birebir", start],
            ["deniz", "AKTARIM", "12.03.2015", "Selin", "Aktarım", "0532 700 00 02", "Anne", guitar, "Aktarim Hocasi", null, start],
            // Kardeş: kayıtlı veliye bağlanır, yeni veli açılmaz. Boş metin hücreleri ("") boş sayılır.
            ["Ece", "Veli", "01.01.2016", "Kayitli", "Veli", "0532 700 00 01", null, "", "", "", ""],
            // Mükerrer: sistemde aynı ad soyad + doğum tarihi.
            ["Mevcut", "Ogrenci", "05.05.2013", null, null, null, null, null, null, null, null],
            // Hatalı: geçersiz telefon ve olmayan enstrüman.
            ["Hatali", "Satir", "01.01.2014", "X", "Y", "12345", null, "Obua", "Aktarim Hocasi", null, null]);

        var preview = await ReadAsync<SpreadsheetImport.Report>(await admin.PostAsync("/api/imports/students/preview", content));
        Assert.False(preview.Committed);
        Assert.Equal(3, preview.Ready);
        Assert.Equal(1, preview.Skipped);
        Assert.Equal(1, preview.Errors);
        Assert.Contains("telefon", preview.Rows.Single(row => row.Status == SpreadsheetImport.RowStatus.Error).Message);
        Assert.False(await db.Students.AnyAsync(s => s.LastName == "Aktarım"));

        using var again = Workbook(StudentHeaders,
            ["Deniz", "Aktarım", new DateTime(2015, 3, 12), "Selin", "Aktarım", "0532 700 00 02", "Anne", piano, "Aktarim Hocasi", "Birebir", start],
            ["deniz", "AKTARIM", "12.03.2015", "Selin", "Aktarım", "0532 700 00 02", "Anne", guitar, "Aktarim Hocasi", null, start],
            ["Ece", "Veli", "01.01.2016", "Kayitli", "Veli", "0532 700 00 01", null, "", "", "", ""],
            ["Mevcut", "Ogrenci", "05.05.2013", null, null, null, null, null, null, null, null],
            ["Hatali", "Satir", "01.01.2014", "X", "Y", "12345", null, "Obua", "Aktarim Hocasi", null, null]);
        var report = await ReadAsync<SpreadsheetImport.Report>(await admin.PostAsync("/api/imports/students", again));
        Assert.True(report.Committed);
        Assert.Equal(3, report.Imported);

        var deniz = await db.Students.AsNoTracking().SingleAsync(s => s.FirstName == "Deniz" && s.LastName == "Aktarım");
        Assert.Equal(new DateOnly(2015, 3, 12), deniz.BirthDate);
        Assert.Equal(2, await db.Enrollments.CountAsync(e => e.StudentId == deniz.Id && e.CourseKind == CourseKind.Individual));
        var denizGuardian = await db.StudentGuardians.AsNoTracking().SingleAsync(link => link.StudentId == deniz.Id);
        Assert.True(denizGuardian.IsPrimary);
        Assert.Equal("+905327000002", (await db.Guardians.AsNoTracking().SingleAsync(g => g.Id == denizGuardian.GuardianId)).PhoneNumber);

        var ece = await db.Students.AsNoTracking().SingleAsync(s => s.FirstName == "Ece" && s.LastName == "Veli");
        Assert.Equal(knownGuardian.Id, (await db.StudentGuardians.AsNoTracking().SingleAsync(link => link.StudentId == ece.Id)).GuardianId);
        Assert.Equal(1, await db.Students.CountAsync(s => s.FirstName == "Mevcut"));
        Assert.False(await db.Students.AnyAsync(s => s.FirstName == "Hatali"));
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "import.students"));
        Assert.Equal(2, await db.AuditLogs.CountAsync(log => log.Action == "enrollment.created"
            && db.Enrollments.Any(e => e.Id == log.EntityId && e.StudentId == deniz.Id)));

        // Aynı dosya ikinci kez: hepsi artık sistemde, hiçbir şey eklenmez.
        using var third = Workbook(StudentHeaders,
            ["Deniz", "Aktarım", new DateTime(2015, 3, 12), "Selin", "Aktarım", "0532 700 00 02", "Anne", piano, "Aktarim Hocasi", "Birebir", start]);
        var replay = await ReadAsync<SpreadsheetImport.Report>(await admin.PostAsync("/api/imports/students", third));
        Assert.Equal(0, replay.Imported);
        Assert.Equal(1, replay.Skipped);
    }

    [Fact]
    public async Task Paid_months_are_recorded_through_the_collection_core_and_cannot_exceed_the_tariff()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await AdminAsync();
        var (piano, _) = await CreateTeacherAsync(admin, "Odeme", "Hocasi");
        using (var students = Workbook(StudentHeaders,
                   ["Mert", "Gecmis", "01.01.2014", null, null, null, null, piano, "Odeme Hocasi", "Birebir", new DateTime(2026, 3, 1)]))
        {
            Assert.Equal(1, (await ReadAsync<SpreadsheetImport.Report>(await admin.PostAsync("/api/imports/students", students))).Imported);
        }
        var enrollment = await db.Enrollments.AsNoTracking()
            .SingleAsync(e => db.Students.Any(s => s.Id == e.StudentId && s.FirstName == "Mert" && s.LastName == "Gecmis"));

        object?[][] rows =
        [
            // Tarife 6.000; 5.500 alınmış - fark indirim olarak donar, ay ödendi sayılır.
            ["Mert", "Gecmis", piano, "2026-09", 5500m, new DateTime(2026, 9, 5), "Havale"],
            // Aynı ay ikinci kez: mükerrer.
            ["Mert", "Gecmis", piano, "Eylül 2026", 5500m, new DateTime(2026, 9, 6), "Nakit"],
            // Tarifeyi aşan tutar: invariant ihlali, yazılmaz.
            ["Mert", "Gecmis", piano, "10.2026", 7000m, new DateTime(2026, 10, 5), null],
            // Kurs kaydı olmayan öğrenci.
            ["Yok", "Boyle", piano, "2026-09", 6000m, new DateTime(2026, 9, 5), null],
        ];

        using (var preview = Workbook(PaymentHeaders, rows))
        {
            var report = await ReadAsync<SpreadsheetImport.Report>(await admin.PostAsync("/api/imports/payments/preview", preview));
            Assert.Equal(1, report.Ready);
            Assert.Equal(1, report.Skipped);
            Assert.Equal(2, report.Errors);
            Assert.Contains("tarife toplamını aşamaz", report.Rows.Single(row => row.Row == 4).Message);
            Assert.False(await db.Payments.AnyAsync(p => db.Receivables.Any(r => r.Id == p.ReceivableId && r.EnrollmentId == enrollment.Id)));
        }

        using (var commit = Workbook(PaymentHeaders, rows))
        {
            var report = await ReadAsync<SpreadsheetImport.Report>(await admin.PostAsync("/api/imports/payments", commit));
            Assert.Equal(1, report.Imported);
            Assert.Equal(2, report.Errors);
        }

        var september = await db.Receivables.AsNoTracking().SingleAsync(r => r.EnrollmentId == enrollment.Id && r.Period == "2026-09");
        Assert.Equal(ReceivableStatus.Paid, september.Status);
        Assert.Equal(6000m, september.BaseAmount);
        Assert.Equal(5500m, september.Amount);
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.ReceivableId == september.Id);
        Assert.Equal(5500m, payment.Amount);
        Assert.Equal(new DateOnly(2026, 9, 5), payment.PaymentDate);
        Assert.Equal(PaymentMethod.Transfer, payment.Method);
        Assert.False(await db.Payments.AnyAsync(p => db.Receivables.Any(r => r.Id == p.ReceivableId && r.EnrollmentId == enrollment.Id && r.Period == "2026-10")));
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "receivable.collection_recorded" && log.EntityId == september.Id));
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "import.payments"));

        // Oturumsuz istek reddedilir.
        var anonymous = _factory.CreateClient();
        using var forbidden = Workbook(PaymentHeaders, rows[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/imports/payments", forbidden)).StatusCode);
    }
}
