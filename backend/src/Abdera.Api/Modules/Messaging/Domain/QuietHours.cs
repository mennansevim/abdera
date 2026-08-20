namespace Abdera.Api.Modules.Messaging.Domain;

// docs/06-whatsapp.md A6: Notifications__QuietHoursStart/End yalnızca zamanlanmış (cron
// kaynaklı) job tiplerine uygulanır. LESSON_REMINDER/LESSON_RESCHEDULED/MAKEUP_APPROVED bu
// kurala tabi DEĞİLDİR (ders saatleri zaten okul saatleri içinde - dersten 1 saat önce gider).
public static class QuietHours
{
    public static readonly IReadOnlySet<NotificationJobType> CronTriggeredTypes = new HashSet<NotificationJobType>
    {
        NotificationJobType.PaymentReminder,
        NotificationJobType.Birthday,
        NotificationJobType.PackageEnding,
    };

    public static bool AppliesTo(NotificationJobType type) => CronTriggeredTypes.Contains(type);

    public static bool IsWithinQuietHours(TimeOnly localTime, TimeOnly quietStart, TimeOnly quietEnd)
    {
        // Gece yarısını aşan pencere (örn. 21:00-09:00): ya başlangıçtan sonra ya da bitişten önce.
        if (quietStart <= quietEnd)
        {
            return localTime >= quietStart && localTime < quietEnd;
        }
        return localTime >= quietStart || localTime < quietEnd;
    }

    /// <summary>
    /// candidateUtc sessiz saat içine denk geliyorsa bir sonraki pencere başlangıcına (quietEnd)
    /// ötelenmiş halini döner; değilse candidateUtc'yi olduğu gibi döner.
    /// </summary>
    public static DateTimeOffset ResolveSendTime(
        DateTimeOffset candidateUtc, TimeZoneInfo schoolTimeZone, TimeOnly quietStart, TimeOnly quietEnd)
    {
        var local = TimeZoneInfo.ConvertTime(candidateUtc, schoolTimeZone);
        var localTime = TimeOnly.FromDateTime(local.DateTime);

        if (!IsWithinQuietHours(localTime, quietStart, quietEnd))
        {
            return candidateUtc;
        }

        var localDate = DateOnly.FromDateTime(local.DateTime);
        // Gece yarısını aşan pencerede ve henüz gece yarısını geçmediysek (örn. saat 22:00,
        // pencere 21:00-09:00), bitiş YARIN'a düşer. Gece yarısını geçtiysek (örn. 03:00),
        // bitiş BUGÜN'e düşer.
        var endDate = quietStart > quietEnd && localTime >= quietStart ? localDate.AddDays(1) : localDate;
        var endLocal = endDate.ToDateTime(quietEnd, DateTimeKind.Unspecified);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, schoolTimeZone);
        return new DateTimeOffset(endUtc, TimeSpan.Zero);
    }
}
