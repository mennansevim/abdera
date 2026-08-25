using Abdera.Api.Modules.People.Domain;

namespace Abdera.Tests.Unit;

public class GuardianLoginCodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_sets_five_minute_expiry_and_zero_attempts()
    {
        var guardianId = Guid.NewGuid();

        var code = GuardianLoginCode.Create(guardianId, "hashed-code", Now);

        Assert.NotEqual(Guid.Empty, code.Id);
        Assert.Equal(guardianId, code.GuardianId);
        Assert.Equal("hashed-code", code.CodeHash);
        Assert.Equal(Now.AddMinutes(5), code.ExpiresAt);
        Assert.Equal(0, code.Attempts);
        Assert.Null(code.ConsumedAt);
        Assert.Equal(Now, code.CreatedAt);
        Assert.Equal(Now, code.UpdatedAt);
    }

    [Fact]
    public void IsUsable_is_true_before_expiry_and_false_at_exact_expiry_boundary()
    {
        var code = GuardianLoginCode.Create(Guid.NewGuid(), "hash", Now);

        Assert.True(code.IsUsable(Now.AddMinutes(5).AddTicks(-1)));
        Assert.False(code.IsUsable(Now.AddMinutes(5)));
    }

    [Fact]
    public void RegisterFailedAttempt_locks_code_after_five_attempts()
    {
        var code = GuardianLoginCode.Create(Guid.NewGuid(), "hash", Now);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            code.RegisterFailedAttempt(Now.AddSeconds(attempt));
            Assert.True(code.IsUsable(Now.AddMinutes(1)));
        }

        code.RegisterFailedAttempt(Now.AddSeconds(5));

        Assert.Equal(5, code.Attempts);
        Assert.Equal(Now.AddSeconds(5), code.UpdatedAt);
        Assert.False(code.IsUsable(Now.AddMinutes(1)));
    }

    [Fact]
    public void MarkConsumed_makes_code_single_use()
    {
        var code = GuardianLoginCode.Create(Guid.NewGuid(), "hash", Now);
        var consumedAt = Now.AddMinutes(1);

        code.MarkConsumed(consumedAt);

        Assert.Equal(consumedAt, code.ConsumedAt);
        Assert.Equal(consumedAt, code.UpdatedAt);
        Assert.False(code.IsUsable(consumedAt));
    }
}
