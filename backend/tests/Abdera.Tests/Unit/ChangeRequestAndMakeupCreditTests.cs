using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

public class ChangeRequestAndMakeupCreditTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LessonChangeRequest_Create_throws_when_proposed_end_before_start()
    {
        Assert.Throws<ArgumentException>(() => LessonChangeRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), "sebep", Now.AddDays(1), Now.AddDays(1).AddMinutes(-10), Now));
    }

    [Fact]
    public void LessonChangeRequest_Approve_sets_status_and_resolved_at()
    {
        var request = LessonChangeRequest.Create(Guid.NewGuid(), Guid.NewGuid(), null, Now.AddDays(1), Now.AddDays(1).AddMinutes(45), Now);
        var resolvedAt = Now.AddHours(2);

        request.Approve(resolvedAt);

        Assert.Equal(LessonChangeRequestStatus.Approved, request.Status);
        Assert.Equal(resolvedAt, request.ResolvedAt);
    }

    [Fact]
    public void LessonChangeRequest_Approve_throws_when_not_pending()
    {
        var request = LessonChangeRequest.Create(Guid.NewGuid(), Guid.NewGuid(), null, Now.AddDays(1), Now.AddDays(1).AddMinutes(45), Now);
        request.Reject(Now);

        Assert.Throws<ConflictException>(() => request.Approve(Now));
    }

    [Fact]
    public void MakeupCredit_Use_transitions_to_used()
    {
        var credit = MakeupCredit.Earn(Guid.NewGuid(), Guid.NewGuid(), MakeupCreditEarnedReason.GuardianCancelled24H, Now, validDays: 60);
        var usedLessonId = Guid.NewGuid();

        credit.Use(usedLessonId, Now.AddDays(5));

        Assert.Equal(MakeupCreditStatus.Used, credit.Status);
        Assert.Equal(usedLessonId, credit.UsedLessonId);
    }

    [Fact]
    public void MakeupCredit_Use_throws_when_already_used()
    {
        var credit = MakeupCredit.Earn(Guid.NewGuid(), Guid.NewGuid(), MakeupCreditEarnedReason.GuardianCancelled24H, Now, validDays: 60);
        credit.Use(Guid.NewGuid(), Now.AddDays(1));

        Assert.Throws<ConflictException>(() => credit.Use(Guid.NewGuid(), Now.AddDays(2)));
    }

    [Fact]
    public void MakeupCredit_Use_throws_when_expired()
    {
        var credit = MakeupCredit.Earn(Guid.NewGuid(), Guid.NewGuid(), MakeupCreditEarnedReason.GuardianCancelled24H, Now, validDays: 60);

        Assert.Throws<ConflictException>(() => credit.Use(Guid.NewGuid(), Now.AddDays(61)));
    }

    [Fact]
    public void MakeupCredit_ExpireIfPastDue_marks_expired_only_when_still_available_and_past_due()
    {
        var credit = MakeupCredit.Earn(Guid.NewGuid(), Guid.NewGuid(), MakeupCreditEarnedReason.SchoolCancelled, Now, validDays: 10);

        credit.ExpireIfPastDue(Now.AddDays(5));
        Assert.Equal(MakeupCreditStatus.Available, credit.Status);

        credit.ExpireIfPastDue(Now.AddDays(11));
        Assert.Equal(MakeupCreditStatus.Expired, credit.Status);
    }
}
