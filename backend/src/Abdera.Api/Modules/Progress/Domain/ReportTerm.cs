namespace Abdera.Api.Modules.Progress.Domain;

// Dönem karnesinin dönemi (issue #15, docs/10-decisions.md Z). Kullanıcı kararı: karne yılda iki
// kez, Ocak ve Haziran'da. Okul takvimine göre 1. dönem Eylül–Ocak, 2. dönem Şubat–Haziran.
// Temmuz–Ağustos hiçbir döneme girmez; o aylarda açılan ekran az önce biten 2. dönemi gösterir.
//
// Anahtar "2025-1" biçimindedir (dönemin başladığı öğretim yılı + dönem no): URL yolunda eğik
// çizgi taşımasın diye "2025-2026/1" yerine bu seçildi; görünen ad Label'dadır.
public readonly record struct ReportTerm(int StartYear, int Number)
{
    public string Key => $"{StartYear}-{Number}";

    public string Label => $"{StartYear}–{StartYear + 1} {Number}. dönem";

    public DateOnly Start => Number == 1 ? new DateOnly(StartYear, 9, 1) : new DateOnly(StartYear + 1, 2, 1);

    // Dahil son gün.
    public DateOnly End => Number == 1 ? new DateOnly(StartYear + 1, 1, 31) : new DateOnly(StartYear + 1, 6, 30);

    public static ReportTerm Containing(DateOnly date) => date.Month switch
    {
        >= 9 => new ReportTerm(date.Year, 1),
        1 => new ReportTerm(date.Year - 1, 1),
        _ => new ReportTerm(date.Year - 1, 2),
    };

    public ReportTerm Previous() => Number == 2 ? new ReportTerm(StartYear, 1) : new ReportTerm(StartYear - 1, 2);

    public static bool TryParse(string? key, out ReportTerm term)
    {
        term = default;
        if (string.IsNullOrWhiteSpace(key)) return false;
        var parts = key.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var number)) return false;
        if (year is < 2000 or > 2999 || number is not (1 or 2)) return false;
        term = new ReportTerm(year, number);
        return true;
    }
}
