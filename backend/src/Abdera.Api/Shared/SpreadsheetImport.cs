using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Abdera.Api.Shared;

// Excel (.xlsx) ile toplu içe aktarma ortak parçaları (docs/10-decisions.md AF): dosyayı okur,
// boş şablon üretir ve satır sonuçlarının biçimini sabitler. Hangi sütunun ne anlama geldiği ve
// satırın nasıl kaydedileceği her modülün kendi Feature dosyasındadır (StudentImport,
// PaymentHistoryImport) - burada iş kuralı yok.
//
// Akış her iki içe aktarmada aynı: aynı dosya önce önizlemeye (hiçbir şey yazılmaz), onaydan
// sonra içe aktarmaya gönderilir; sunucu ikinci seferde dosyayı baştan doğrular, önizlemeye
// güvenmez. Yalnızca "Hazır" satırlar yazılır; hatalı ve mükerrer satırlar atlanıp raporlanır.
public static class SpreadsheetImport
{
    public const long MaxBytes = 2 * 1024 * 1024;
    public const int MaxRows = 1000;

    public enum RowStatus
    {
        Ready,
        Imported,
        Skipped,
        Error,
    }

    public record RowResult(int Row, string Label, RowStatus Status, string Message);

    public record Report(bool Committed, int Total, int Ready, int Imported, int Skipped, int Errors, List<RowResult> Rows)
    {
        public static Report From(bool committed, List<RowResult> rows) => new(
            committed,
            rows.Count,
            rows.Count(row => row.Status == RowStatus.Ready),
            rows.Count(row => row.Status == RowStatus.Imported),
            rows.Count(row => row.Status == RowStatus.Skipped),
            rows.Count(row => row.Status == RowStatus.Error),
            rows.OrderBy(row => row.Row).ToList());
    }

    public record Column(string Header, bool Required, string Description, string Example);

    // Bir veri satırı: Excel'deki satır numarası (başlık 1. satır) ve başlık adına göre hücreler.
    public sealed class Row(int number, IReadOnlyDictionary<string, XLCellValue> cells)
    {
        public int Number { get; } = number;

        public string? Text(string header)
        {
            if (!cells.TryGetValue(header, out var value) || value.IsBlank) return null;
            var text = value.IsNumber ? value.GetNumber().ToString(CultureInfo.InvariantCulture)
                : value.IsDateTime ? value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : value.ToString(CultureInfo.InvariantCulture);
            text = text.Trim();
            return text.Length == 0 ? null : text;
        }

        // Excel tarih hücresi ya da "12.03.2015", "12/03/2015", "2015-03-12" metni.
        public DateOnly? Date(string header, out string? error)
        {
            error = null;
            // Boş metin hücresi ("") boş sayılır - Excel'de silinmiş hücre böyle kalabiliyor.
            if (!cells.TryGetValue(header, out var value) || Text(header) is not { } text) return null;
            if (value.IsDateTime) return DateOnly.FromDateTime(value.GetDateTime());
            if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
            error = $"{header}: '{text}' tarih olarak okunamadı (ör. 12.03.2015).";
            return null;
        }

        // Excel sayı hücresi ya da "1.200,50" / "1200.50" / "1200 TL" metni.
        public decimal? Amount(string header, out string? error)
        {
            error = null;
            if (!cells.TryGetValue(header, out var value) || Text(header) is not { } text) return null;
            if (value.IsNumber) return Math.Round((decimal)value.GetNumber(), 2);
            if (TryParseAmount(text, out var amount)) return amount;
            error = $"{header}: '{text}' tutar olarak okunamadı (ör. 6000 ya da 6.000,00).";
            return null;
        }
    }

    private static readonly string[] DateFormats =
        ["d.M.yyyy", "dd.MM.yyyy", "d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d-M-yyyy", "dd-MM-yyyy"];

    // Ayraçlar Türkçe Excel'de "1.200,50", dışa aktarılmış metinde "1200.50" gelir. Son ayraç
    // ondalık sayılır; virgülsüz tek nokta ve ardından üç hane binlik ayracıdır ("6.000").
    public static bool TryParseAmount(string text, out decimal amount)
    {
        var cleaned = new string(text.Where(ch => char.IsDigit(ch) || ch is '.' or ',' or '-').ToArray());
        amount = 0m;
        if (cleaned.Length == 0) return false;

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        string normalized;
        if (lastComma >= 0 && lastDot >= 0)
        {
            var decimalSeparator = lastComma > lastDot ? ',' : '.';
            var thousands = decimalSeparator == ',' ? "." : ",";
            normalized = cleaned.Replace(thousands, "").Replace(decimalSeparator, '.');
        }
        else if (lastComma >= 0)
        {
            normalized = cleaned.Count(ch => ch == ',') == 1 ? cleaned.Replace(',', '.') : cleaned.Replace(",", "");
        }
        else if (lastDot >= 0 && (cleaned.Count(ch => ch == '.') > 1 || cleaned.Length - lastDot - 1 == 3))
        {
            normalized = cleaned.Replace(".", "");
        }
        else
        {
            normalized = cleaned;
        }

        if (!decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
            return false;
        amount = Math.Round(amount, 2);
        return true;
    }

    // Eşleştirme anahtarı: büyük/küçük harf, Türkçe i/ı/İ/I ve fazla boşluk farkı yok sayılır.
    // CultureInfo("tr-TR") kullanılmaz (bkz. PersonNameFormatter'daki gerekçe).
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                continue;
            }
            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(ch switch
            {
                'İ' or 'I' or 'ı' => 'i',
                _ => char.ToLowerInvariant(ch),
            });
        }
        return builder.ToString();
    }

    public static async Task<List<Row>> ReadAsync(IFormFile? file, IReadOnlyList<Column> columns)
    {
        if (file is null || file.Length == 0)
            throw Invalid("Excel dosyası gönderilmedi.");
        if (file.Length > MaxBytes)
            throw Invalid("Dosya en fazla 2 MB olabilir.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        buffer.Position = 0;

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(buffer);
        }
        catch (Exception)
        {
            throw Invalid("Dosya okunamadı. Excel'de .xlsx (Excel Çalışma Kitabı) olarak kaydedip tekrar deneyin.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw Invalid("Dosyada sayfa yok.");
            var used = sheet.RangeUsed();
            if (used is null) throw Invalid("Dosya boş.");

            var headerRow = used.FirstRow();
            var headerIndex = new Dictionary<string, int>();
            foreach (var cell in headerRow.Cells())
            {
                var key = Fold(cell.GetString());
                var column = columns.FirstOrDefault(column => Fold(column.Header) == key);
                if (column is not null && !headerIndex.ContainsKey(column.Header))
                    headerIndex[column.Header] = cell.Address.ColumnNumber;
            }

            var missing = columns.Where(column => column.Required && !headerIndex.ContainsKey(column.Header))
                .Select(column => column.Header).ToList();
            if (missing.Count > 0)
                throw Invalid($"Şu sütunlar bulunamadı: {string.Join(", ", missing)}. Boş şablonu indirip onunla doldurun.");

            var rows = new List<Row>();
            var firstDataRow = headerRow.RowNumber() + 1;
            var lastRow = used.LastRow().RowNumber();
            for (var number = firstDataRow; number <= lastRow; number++)
            {
                var cells = headerIndex.ToDictionary(pair => pair.Key, pair => sheet.Cell(number, pair.Value).Value);
                if (cells.Values.All(value => value.IsBlank || (value.IsText && string.IsNullOrWhiteSpace(value.GetText()))))
                    continue;
                rows.Add(new Row(number, cells));
            }

            if (rows.Count == 0) throw Invalid("Dosyada başlık satırından sonra veri yok.");
            if (rows.Count > MaxRows) throw Invalid($"Tek seferde en fazla {MaxRows} satır içe aktarılabilir.");
            return rows;
        }
    }

    // İlk sayfa yalnızca başlık satırı taşır (örnek satır yanlışlıkla içe aktarılmasın);
    // ikinci sayfa her sütunu ve örnek değerini anlatır.
    public static IResult Template(string fileName, string sheetName, IReadOnlyList<Column> columns)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var index = 0; index < columns.Count; index++)
        {
            var cell = sheet.Cell(1, index + 1);
            cell.Value = columns[index].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = columns[index].Required ? XLColor.FromHtml("#FDE7C8") : XLColor.FromHtml("#EEF1F5");
            sheet.Column(index + 1).Width = Math.Max(16, columns[index].Header.Length + 4);
        }
        sheet.SheetView.FreezeRows(1);

        var guide = workbook.Worksheets.Add("Nasıl doldurulur");
        string[] guideHeaders = ["Sütun", "Zorunlu", "Açıklama", "Örnek"];
        for (var index = 0; index < guideHeaders.Length; index++)
        {
            guide.Cell(1, index + 1).Value = guideHeaders[index];
            guide.Cell(1, index + 1).Style.Font.Bold = true;
        }
        for (var index = 0; index < columns.Count; index++)
        {
            guide.Cell(index + 2, 1).Value = columns[index].Header;
            guide.Cell(index + 2, 2).Value = columns[index].Required ? "Evet" : "Hayır";
            guide.Cell(index + 2, 3).Value = columns[index].Description;
            guide.Cell(index + 2, 4).Value = columns[index].Example;
        }
        guide.Column(1).Width = 22;
        guide.Column(2).Width = 10;
        guide.Column(3).Width = 80;
        guide.Column(4).Width = 22;
        guide.Column(3).Style.Alignment.WrapText = true;

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return Results.File(output.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // Satır hatası olarak gösterilecek ilk okunabilir mesaj.
    public static string MessageOf(Exception exception) => exception switch
    {
        ValidationFailedException validation => validation.Errors.Values.SelectMany(messages => messages).FirstOrDefault() ?? "Doğrulama hatası.",
        ArgumentException argument => argument.Message.Split(" (Parameter")[0],
        _ => exception.Message,
    };

    private static ValidationFailedException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["file"] = [message] });
}
