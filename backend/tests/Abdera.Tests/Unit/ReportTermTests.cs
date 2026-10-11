using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

// Issue #15 - karne dönemleri: 1. dönem Eylül–Ocak, 2. dönem Şubat–Haziran; Temmuz–Ağustos
// az önce biten 2. döneme bağlanır.
public class ReportTermTests
{
    [Theory]
    [InlineData(2025, 9, 1, "2025-1")]
    [InlineData(2025, 12, 31, "2025-1")]
    [InlineData(2026, 1, 31, "2025-1")]
    [InlineData(2026, 2, 1, "2025-2")]
    [InlineData(2026, 6, 30, "2025-2")]
    [InlineData(2026, 8, 15, "2025-2")]
    [InlineData(2026, 9, 1, "2026-1")]
    public void Containing_maps_dates_to_school_terms(int year, int month, int day, string expectedKey)
    {
        Assert.Equal(expectedKey, ReportTerm.Containing(new DateOnly(year, month, day)).Key);
    }

    [Fact]
    public void Term_boundaries_labels_and_previous_term()
    {
        var first = new ReportTerm(2025, 1);
        Assert.Equal((new DateOnly(2025, 9, 1), new DateOnly(2026, 1, 31)), (first.Start, first.End));
        Assert.Equal("2025–2026 1. dönem", first.Label);
        var second = new ReportTerm(2025, 2);
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 6, 30)), (second.Start, second.End));
        Assert.Equal(first, second.Previous());
        Assert.Equal(new ReportTerm(2024, 2), first.Previous());
    }

    [Theory]
    [InlineData("2025-1", true)]
    [InlineData("2025-2", true)]
    [InlineData("2025-3", false)]
    [InlineData("2025/1", false)]
    [InlineData("abc-1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Keys_are_parsed_strictly(string? key, bool valid)
    {
        Assert.Equal(valid, ReportTerm.TryParse(key, out _));
    }

    [Fact]
    public void A_report_needs_a_comment_to_publish_and_is_locked_while_published()
    {
        var now = DateTimeOffset.UtcNow;
        var report = TermReport.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new ReportTerm(2025, 1), now);
        Assert.Throws<ValidationFailedException>(() => report.Publish("{}", Guid.NewGuid(), now));

        report.SaveComment("  Güzel bir dönem.  ", now);
        Assert.Equal("Güzel bir dönem.", report.Comment);
        report.Publish("{}", Guid.NewGuid(), now);
        Assert.Equal(TermReportStatus.Published, report.Status);
        Assert.Throws<ConflictException>(() => report.SaveComment("değişti", now));
        Assert.Throws<ConflictException>(() => report.Publish("{}", Guid.NewGuid(), now));

        report.Unpublish(now);
        Assert.Equal(TermReportStatus.Draft, report.Status);
        Assert.Null(report.SnapshotJson);
        Assert.Throws<ValidationFailedException>(() => report.SaveComment(new string('x', TermReport.CommentMaxLength + 1), now));
    }
}
