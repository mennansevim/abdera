using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

public class EngagementDomainTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void Practice_journal_rejects_invalid_duration(int duration)
    {
        Assert.Throws<ValidationFailedException>(() => PracticeJournalEntry.Create(
            Guid.NewGuid(), new DateOnly(2026, 8, 25), duration, "Gam", null, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Practice_journal_parent_approval_is_explicit()
    {
        var guardianId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var entry = PracticeJournalEntry.Create(
            Guid.NewGuid(), new DateOnly(2026, 8, 25), 30, "  Gam  ", "  80 BPM  ", Guid.NewGuid(), now);

        Assert.Null(entry.ParentApprovedAt);
        entry.Approve(guardianId, now.AddMinutes(1));

        Assert.Equal("Gam", entry.Goal);
        Assert.Equal("80 BPM", entry.Note);
        Assert.Equal(guardianId, entry.ParentApprovedByGuardianId);
    }

    [Fact]
    public void Maintenance_period_advances_beyond_current_time()
    {
        var now = new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);
        var setting = InstrumentMaintenanceSetting.Create(
            Guid.NewGuid(), "Tel değişimi", 30, true, MaintenanceNotificationPreference.WhatsApp,
            now.AddDays(-95), now.AddDays(-100));

        setting.AdvanceAfter(now);

        Assert.True(setting.NextReminderAt > now);
        Assert.Equal(now.AddDays(25), setting.NextReminderAt);
    }
}
