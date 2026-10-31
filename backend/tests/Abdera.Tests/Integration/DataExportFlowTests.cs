using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Shared;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Tüm verinin Excel dökümü (docs/10-decisions.md AJ): istek arka planda tamamlanır, dosyanın
// Öğrenciler/Ödemeler sayfaları içe aktarma ekranına olduğu gibi geri verilebilir ve yalnızca son
// üç döküm tutulur.
public class DataExportFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public DataExportFlowTests(AbderaWebApplicationFactory factory)
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
        Assert.True(response.IsSuccessStatusCode, $"Beklenmeyen durum: {(int)response.StatusCode} {response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private static MultipartFormDataContent File(byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new MultipartFormDataContent { { file, "file", "dokum.xlsx" } };
    }

    private static byte[] Sheet(string name, string[] headers, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(name);
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        for (var row = 0; row < rows.Length; row++)
        for (var column = 0; column < rows[row].Length; column++)
            sheet.Cell(row + 2, column + 1).Value = rows[row][column] switch
            {
                null => Blank.Value,
                decimal amount => amount,
                var value => value.ToString(),
            };
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task<DataExports.DataExportResponse> ExportAsync(HttpClient admin)
    {
        var requested = await admin.PostAsync("/api/data-exports", null);
        var export = await ReadAsync<DataExports.DataExportResponse>(requested);
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var list = await ReadAsync<List<DataExports.DataExportResponse>>(await admin.GetAsync("/api/data-exports"));
            var current = list.SingleOrDefault(e => e.Id == export.Id);
            if (current is { Status: DataExportStatus.Completed or DataExportStatus.Failed }) return current;
            await Task.Delay(250);
        }
        throw new TimeoutException("Döküm 30 saniyede tamamlanmadı.");
    }

    [Fact]
    public async Task Export_runs_in_the_background_and_its_sheets_feed_back_into_the_importers()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await AdminAsync();
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(i => i.Code == "PIANO");
        var guitar = instruments.Single(i => i.Code == "GUITAR");
        await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/teachers", new Teachers.CreateRequest("Doku", "Hocasi", [piano.Id, guitar.Id], null)));

        // Durum sütunları: pasif öğrenci ve bitmiş kurs olduğu gibi açılır, bitmiş kursa aidat açılmaz.
        string[] studentHeaders = [.. StudentImport.Columns.Select(c => c.Header)];
        var students = await ReadAsync<SpreadsheetImport.Report>(await admin.PostAsync("/api/imports/students", File(Sheet("Sayfa1", studentHeaders,
            ["Ada", "Doku", "01.01.2014", "Ece", "Doku", "0532 710 00 01", "Anne", piano.Name, "Doku Hocasi", "Birebir", "01.03.2026", "Aktif", "Evet", "Aktif", null],
            ["Bora", "Doku", "02.02.2013", "Ece", "Doku", "0532 710 00 01", "Anne", guitar.Name, "Doku Hocasi", "Birebir", "01.01.2026", "Pasif", "Hayır", "Bitti", "30.06.2026"],
            ["Cem", "Doku", "03.03.2012", null, null, null, null, piano.Name, "Doku Hocasi", null, null, null, null, "Bitti", null]))));
        Assert.Equal(2, students.Imported);
        Assert.Contains("zorunlu", students.Rows.Single(r => r.Status == SpreadsheetImport.RowStatus.Error).Message);

        var ada = await db.Students.AsNoTracking().SingleAsync(s => s.FirstName == "Ada" && s.LastName == "Doku");
        var bora = await db.Students.AsNoTracking().SingleAsync(s => s.FirstName == "Bora" && s.LastName == "Doku");
        Assert.True(ada.SiblingDiscount);
        Assert.Equal(StudentStatus.Inactive, bora.Status);
        var boraCourse = await db.Enrollments.AsNoTracking().SingleAsync(e => e.StudentId == bora.Id);
        Assert.Equal(EnrollmentStatus.Ended, boraCourse.Status);
        Assert.Equal(new DateOnly(2026, 6, 30), boraCourse.EndedAt);
        Assert.False(await db.Receivables.AnyAsync(r => r.EnrollmentId == boraCourse.Id));

        string[] paymentHeaders = [.. PaymentHistoryImport.Columns.Select(c => c.Header)];
        var payments = await ReadAsync<SpreadsheetImport.Report>(await admin.PostAsync("/api/imports/payments", File(Sheet("Sayfa1", paymentHeaders,
            ["Ada", "Doku", piano.Name, "Doku Hocasi", "2026-09", 6000m, "05.09.2026", "Havale", null]))));
        Assert.True(payments.Imported == 1, string.Join(" | ", payments.Rows.Select(r => r.Message)));

        var export = await ExportAsync(admin);
        Assert.Equal(DataExportStatus.Completed, export.Status);
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "data_export.requested" && log.EntityId == export.Id));

        var download = await admin.GetAsync($"/api/data-exports/{export.Id}/file");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        var bytes = await download.Content.ReadAsByteArrayAsync();

        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            Assert.Equal(
                ["Öğrenciler", "Ödemeler", "Öğretmenler", "Veliler", "Aidatlar", "Tahsilatlar", "Dersler", "Giderler", "Bu dosya hakkında"],
                workbook.Worksheets.Select(sheet => sheet.Name).ToArray());
            var studentSheet = workbook.Worksheet("Öğrenciler");
            Assert.Equal(studentHeaders, Enumerable.Range(1, studentHeaders.Length).Select(c => studentSheet.Cell(1, c).GetString()).ToArray());
            var boraRow = studentSheet.RowsUsed().Single(r => r.Cell(1).GetString() == "Bora" && r.Cell(2).GetString() == "Doku");
            Assert.Equal("Pasif", boraRow.Cell(12).GetString());
            Assert.Equal("Bitti", boraRow.Cell(14).GetString());
            var paymentSheet = workbook.Worksheet("Ödemeler");
            Assert.Contains(paymentSheet.RowsUsed(), r => r.Cell(1).GetString() == "Ada" && r.Cell(5).GetString() == "2026-09");
        }

        // Aynı dosya iki içe aktarmaya da olduğu gibi verilir: herkes sistemde, hiçbir şey çoğalmaz.
        var studentReplay = await ReadAsync<SpreadsheetImport.Report>(await admin.PostAsync("/api/imports/students/preview", File(bytes)));
        Assert.All(studentReplay.Rows.Where(r => r.Label.EndsWith("Doku")), r => Assert.Equal(SpreadsheetImport.RowStatus.Skipped, r.Status));
        Assert.Equal(2, studentReplay.Rows.Count(r => r.Label.EndsWith("Doku")));
        var paymentReplay = await ReadAsync<SpreadsheetImport.Report>(await admin.PostAsync("/api/imports/payments/preview", File(bytes)));
        Assert.Equal(SpreadsheetImport.RowStatus.Skipped, paymentReplay.Rows.Single(r => r.Label.StartsWith("Ada Doku")).Status);

        // Oturumsuz istek reddedilir.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsync("/api/data-exports", null)).StatusCode);
    }

    [Fact]
    public async Task Only_the_latest_three_exports_are_kept()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await AdminAsync();

        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++) ids.Add((await ExportAsync(admin)).Id);

        var list = await ReadAsync<List<DataExports.DataExportResponse>>(await admin.GetAsync("/api/data-exports"));
        Assert.Equal(ids.Skip(1).Reverse(), list.Select(e => e.Id));
        Assert.Equal(DataExport.KeepLatest, await db.DataExports.CountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/data-exports/{ids[0]}/file")).StatusCode);
    }
}
