namespace Abdera.Api.Modules.People.Domain;

// Ayrılma riski erken uyarısı (issue #14). Saf fonksiyon: sayıları çağıran taraf
// (AttentionNeededStudents) toplar, karar ve gerekçe metni burada üretilir. Her sinyal
// bir skor değil, okunabilir bir NEDEN üretir - yönetici "neden bu öğrenci?" sorusuna
// başka ekrana gitmeden cevap bulabilmeli.
public record AttentionSignal(bool NeedsAttention, IReadOnlyList<string> Reasons)
{
    public const int AbsenceWindowDays = 30;
    public const int AbsenceThreshold = 2;
    public const int ConcernThreshold = 2;
    public const int RsvpWindowDays = 28;
    public const int NotAttendingThreshold = 2;
    public const int LateThreshold = 3;
    // Pratik günlüğü: son 3 hafta boşken, ondan önceki 8 haftada en az 2 kayıt varsa sinyal.
    // Günlüğü hiç kullanmayan aile için sinyal üretilmez - "hiç girmemek" ayrılma işareti değil,
    // "girerken bırakmak" öyle.
    public const int PracticeQuietDays = 21;
    public const int PracticePriorWindowDays = 56;
    public const int PracticePriorMinimumEntries = 2;
    // Kullanıcı kararı (2026-10-11): 1-2 ay geç ödeyen aile az değil; 2 aylık eşikte liste bir
    // aidat listesine dönüşüp asıl uyarıları gömerdi. Yalnızca ciddi birikme sinyal üretir.
    public const int OverdueThreshold = 3;
    // Yalnızca yakın zamanda puanı düşen yetenek; aylar önceki bir düşüş bugünün sinyali değil.
    public const int SkillDropWindowDays = 60;

    public record SkillChange(string SkillLabel, int PreviousScore, int LatestScore);

    // OverdueReceivableCount null: isteyen kişi aidat verisini göremez (öğretmen,
    // docs/04-permissions.md) - bu durumda aidat sinyali hiç değerlendirilmez.
    public record Inputs(
        int RecentAbsenceCount,
        int RecentConcernCommentCount = 0,
        int RecentNotAttendingRsvpCount = 0,
        int RecentLateRsvpCount = 0,
        int PracticeEntriesRecent = 0,
        int PracticeEntriesPrior = 0,
        int? OverdueReceivableCount = null,
        IReadOnlyList<SkillChange>? SkillChanges = null);

    public static AttentionSignal Evaluate(int recentAbsenceCount, int recentConcernCommentCount) =>
        Evaluate(new Inputs(recentAbsenceCount, recentConcernCommentCount));

    public static AttentionSignal Evaluate(Inputs inputs)
    {
        var reasons = new List<string>();
        if (inputs.RecentAbsenceCount >= AbsenceThreshold)
            reasons.Add($"Son {AbsenceWindowDays} günde {inputs.RecentAbsenceCount} devamsızlık");
        if (inputs.RecentConcernCommentCount >= ConcernThreshold)
            reasons.Add($"Son 4 yorumun {inputs.RecentConcernCommentCount} tanesinde dikkat işareti");
        if (inputs.RecentNotAttendingRsvpCount >= NotAttendingThreshold)
            reasons.Add($"Son 4 haftada {inputs.RecentNotAttendingRsvpCount} kez \"gelemiyorum\" dedi");
        if (inputs.RecentLateRsvpCount >= LateThreshold)
            reasons.Add($"Son 4 haftada {inputs.RecentLateRsvpCount} kez \"geç kalacağım\" dedi");
        if (inputs.PracticeEntriesRecent == 0 && inputs.PracticeEntriesPrior >= PracticePriorMinimumEntries)
            reasons.Add("3 haftadır pratik günlüğü girilmedi (önceden düzenliydi)");
        if (inputs.OverdueReceivableCount is { } overdue && overdue >= OverdueThreshold)
            reasons.Add($"{overdue} aylık aidat gecikmiş");

        var drops = (inputs.SkillChanges ?? [])
            .Where(change => change.LatestScore < change.PreviousScore)
            .Select(change => $"{change.SkillLabel} {change.PreviousScore} → {change.LatestScore}")
            .ToList();
        if (drops.Count > 0)
            reasons.Add($"Yetenek puanı düştü: {string.Join(", ", drops)}");

        return new AttentionSignal(reasons.Count > 0, reasons);
    }
}
