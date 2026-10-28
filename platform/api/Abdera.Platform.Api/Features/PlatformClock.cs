namespace Abdera.Platform.Api.Features;

// Platformun "bugün"ü okulların saat dilimindedir (Platform:TimeZone, varsayılan Europe/Istanbul): lisans
// gün sınırı UTC gece yarısında değil, yerel gece yarısında döner.
public static class PlatformClock
{
    public static DateOnly Today(IConfiguration config, TimeProvider time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(),
            TimeZoneInfo.FindSystemTimeZoneById(config["Platform:TimeZone"] is { Length: > 0 } zone ? zone : "Europe/Istanbul")).DateTime);
}
