using Abdera.Api.Modules.Instance.Domain;
using Abdera.Api.Shared;
using Abdera.Api.Shared.Modules;
using Microsoft.Extensions.Configuration;

namespace Abdera.Tests.Unit;

public class InstanceDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_module_list_keeps_every_module_enabled(string? configured)
    {
        // Bugünkü tek okullu kurulumda Modules:Enabled hiç tanımlı değil - hiçbir ekran kaybolmamalı.
        var enabled = ModuleCatalog.ResolveEnabled(configured);

        Assert.All(ModuleCatalog.All, module => Assert.Contains(module.Key, enabled));
    }

    [Fact]
    public void Core_modules_stay_enabled_when_only_optional_modules_are_listed()
    {
        var enabled = ModuleCatalog.ResolveEnabled("library");

        Assert.Contains(ModuleKeys.Library, enabled);
        Assert.Contains(ModuleKeys.Billing, enabled);
        Assert.Contains(ModuleKeys.People, enabled);
        Assert.DoesNotContain(ModuleKeys.Banking, enabled);
        Assert.DoesNotContain(ModuleKeys.Progress, enabled);
    }

    [Theory]
    [InlineData("none")]
    [InlineData(" NONE ")]
    public void Explicit_none_enables_only_core_modules(string configured)
    {
        // Sihirbazda hiçbir opsiyon seçilmeyen okul boş değerle "hepsi açık"a düşmemeli.
        var enabled = ModuleCatalog.ResolveEnabled(configured);

        Assert.All(ModuleCatalog.All, module => Assert.Equal(module.IsCore, enabled.Contains(module.Key)));
    }

    [Fact]
    public void Unknown_module_key_is_rejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModuleCatalog.ResolveEnabled("library, librray"));
        Assert.Contains("librray", error.Message);
    }

    [Fact]
    public void Every_dependency_in_the_catalog_points_to_a_known_module()
    {
        Assert.All(ModuleCatalog.All.SelectMany(module => module.Requires),
            required => Assert.NotNull(ModuleCatalog.Find(required)));
    }

    [Fact]
    public void Missing_profile_row_reads_as_the_current_abdera_identity()
    {
        var profile = SchoolProfile.CreateDefault(Now);

        Assert.Equal("Abdera Sanat Akademi", profile.Name);
        Assert.Equal("Abdera", profile.ShortName);
        Assert.Null(profile.PrimaryColor);
        Assert.False(profile.HasLogo);
    }

    [Theory]
    [InlineData("", "Kısa", null, "name")]
    [InlineData("Okul", "", null, "shortName")]
    [InlineData("Okul", "Kısa", "mor", "primaryColor")]
    [InlineData("Okul", "Kısa", "#12345", "primaryColor")]
    public void Invalid_identity_is_rejected(string name, string shortName, string? color, string field)
    {
        var profile = SchoolProfile.CreateDefault(Now);

        var error = Assert.Throws<ValidationFailedException>(() => profile.UpdateIdentity(name, shortName, color, null, Now));
        Assert.Contains(field, error.Errors.Keys);
    }

    [Fact]
    public void Color_is_normalised_to_lower_case()
    {
        var profile = SchoolProfile.CreateDefault(Now);

        profile.UpdateIdentity(" Do Re Mi Müzik Evi ", "Do Re Mi", "#5B3FA8", null, Now);

        Assert.Equal("Do Re Mi Müzik Evi", profile.Name);
        Assert.Equal("#5b3fa8", profile.PrimaryColor);
    }

    [Fact]
    public void Logo_type_comes_from_file_bytes_not_from_the_claimed_type()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0];
        byte[] webp = [.."RIFF"u8, 0, 0, 0, 0, .."WEBP"u8];
        byte[] svg = [.."<svg onload=alert(1)>"u8];

        Assert.Equal("image/png", SchoolProfile.SniffLogoContentType(png));
        Assert.Equal("image/jpeg", SchoolProfile.SniffLogoContentType(jpeg));
        Assert.Equal("image/webp", SchoolProfile.SniffLogoContentType(webp));
        Assert.Null(SchoolProfile.SniffLogoContentType(svg));
    }

    [Fact]
    public void Oversized_or_unknown_logo_is_rejected_and_a_new_logo_gets_a_new_version()
    {
        var profile = SchoolProfile.CreateDefault(Now);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

        Assert.Throws<ValidationFailedException>(() => profile.ReplaceLogo([.."%PDF-1.4"u8], null, Now));
        Assert.Throws<ValidationFailedException>(() => profile.ReplaceLogo(new byte[SchoolProfile.MaxLogoBytes + 1], null, Now));

        profile.ReplaceLogo(png, null, Now);
        var firstVersion = profile.LogoVersion;
        profile.ReplaceLogo(png, null, Now);

        Assert.True(profile.HasLogo);
        Assert.NotEqual(firstVersion, profile.LogoVersion);

        profile.RemoveLogo(null, Now);
        Assert.False(profile.HasLogo);
        Assert.Null(profile.LogoContentType);
        Assert.Null(profile.LogoVersion);
    }

    [Fact]
    public void Missing_policy_row_reads_as_todays_rules()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Policy:MakeupCreditValidDays"] = "45",
        }).Build();

        var policy = SchoolPolicy.CreateDefault(config, Now);

        Assert.Equal(40m, policy.GroupLessonSharePercent);
        Assert.Equal(4, policy.MaxWeeklyLessons);
        Assert.Equal(24, policy.MakeupNoticeHours);
        Assert.Equal(45, policy.MakeupCreditValidDays);
        Assert.Equal(21, policy.ExcusedMakeupWindowDays);
        Assert.Equal(new TimeOnly(9, 0), policy.SchoolDayStart);
        Assert.Equal(new TimeOnly(21, 0), policy.SchoolDayEnd);
    }

    [Theory]
    [InlineData(101, 4, "09:00", "21:00", "groupLessonSharePercent")]
    [InlineData(40.123, 4, "09:00", "21:00", "groupLessonSharePercent")]
    [InlineData(40, 0, "09:00", "21:00", "maxWeeklyLessons")]
    [InlineData(40, 4, "21:00", "09:00", "schoolDayEnd")]
    [InlineData(40, 4, "09:10", "21:00", "schoolDayStart")]
    public void Invalid_policy_is_rejected(double share, int maxWeekly, string start, string end, string field)
    {
        var policy = SchoolPolicy.CreateDefault(null, Now);

        var error = Assert.Throws<ValidationFailedException>(() => policy.Update(
            (decimal)share, maxWeekly, 24, 60, 21, TimeOnly.Parse(start), TimeOnly.Parse(end), null, Now));

        Assert.Contains(field, error.Errors.Keys);
    }
}
