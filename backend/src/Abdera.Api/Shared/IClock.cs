namespace Abdera.Api.Shared;

// CLAUDE.md: veritabanında her zaman timestamptz (UTC instant). Yerel gösterim/hesaplama
// Europe/Istanbul ile uygulama katmanında yapılır. Testte sabit zaman enjekte edebilmek
// için DateTimeOffset.UtcNow'ı doğrudan çağırmak yerine bu soyutlama kullanılır.
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    TimeZoneInfo SchoolTimeZone { get; }
    DateTimeOffset ToSchoolLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, SchoolTimeZone);
}

public class SystemClock : IClock
{
    public SystemClock(IConfiguration configuration)
    {
        var tzId = configuration["SCHOOL_TIMEZONE"] ?? "Europe/Istanbul";
        SchoolTimeZone = TimeZoneInfo.FindSystemTimeZoneById(tzId);
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public TimeZoneInfo SchoolTimeZone { get; }
}
