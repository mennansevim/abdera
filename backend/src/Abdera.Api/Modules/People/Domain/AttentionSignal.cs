namespace Abdera.Api.Modules.People.Domain;

public record AttentionSignal(bool NeedsAttention, IReadOnlyList<string> Reasons)
{
    public static AttentionSignal Evaluate(int recentAbsenceCount, int recentConcernCommentCount)
    {
        var reasons = new List<string>();
        if (recentAbsenceCount >= 2)
            reasons.Add($"Son 30 günde {recentAbsenceCount} devamsızlık");
        if (recentConcernCommentCount >= 2)
            reasons.Add($"Son 4 yorumun {recentConcernCommentCount} tanesinde dikkat işareti");
        return new AttentionSignal(reasons.Count > 0, reasons);
    }
}
