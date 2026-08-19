using Abdera.Api.Modules.Auth.Domain;

namespace Abdera.Tests.Unit;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_email_to_lowercase_and_trims()
    {
        var user = User.Create("  Admin@Abdera.Test  ", "hash", UserRole.Admin, Now);

        Assert.Equal("admin@abdera.test", user.Email);
    }

    [Fact]
    public void Create_throws_when_email_is_empty()
    {
        Assert.Throws<ArgumentException>(() => User.Create("   ", "hash", UserRole.Admin, Now));
    }

    [Fact]
    public void Create_sets_active_and_timestamps()
    {
        var user = User.Create("teacher@abdera.test", "hash", UserRole.Teacher, Now);

        Assert.True(user.IsActive);
        Assert.Equal(Now, user.CreatedAt);
        Assert.Equal(Now, user.UpdatedAt);
        Assert.False(user.MustChangePassword);
    }

    [Fact]
    public void SetPassword_updates_hash_and_must_change_flag()
    {
        var user = User.Create("teacher@abdera.test", "hash", UserRole.Teacher, Now);
        var later = Now.AddDays(1);

        user.SetPassword("new-hash", later, mustChangePassword: true);

        Assert.Equal("new-hash", user.PasswordHash);
        Assert.True(user.MustChangePassword);
        Assert.Equal(later, user.UpdatedAt);
    }

    [Fact]
    public void Deactivate_sets_is_active_false_and_bumps_updated_at()
    {
        var user = User.Create("teacher@abdera.test", "hash", UserRole.Teacher, Now);
        var later = Now.AddDays(2);

        user.Deactivate(later);

        Assert.False(user.IsActive);
        Assert.Equal(later, user.UpdatedAt);
    }
}
