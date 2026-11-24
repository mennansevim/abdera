using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// docs/10-decisions.md AM: seri kurulurken yalnızca GenerationWeeks haftalık ders üretiliyordu ve
// pencereyi ileri taşıyan bir iş yoktu - Eylül sonunda girilen program Kasım sonunda takvimden
// boşaldı. Bu iş her açık seriyi okul yılı sonuna kadar uzatır. Yalnızca EKSİK günleri açar:
// var olan derse (iptal, taşınmış, telafi zinciri dahil) dokunmaz; tatil ve öğretmen izni
// atlanır; bitmiş/donmuş kayıt ve pasif öğrenci uzatılmaz. Bugünün dersi açılmaz (yarından başlar).
public static class SeriesExtension
{
    public record Result(int SeriesVisited, int LessonsCreated, DateOnly Until);

    // Okul yılı Haziran sonunda biter. Temmuz'dan itibaren bir sonraki okul yılının sonu hedeflenir.
    // Scheduling:SchoolYearEnd (yyyy-MM-dd) verilirse ve geçmemişse o tarih kullanılır.
    public static DateOnly SchoolYearEnd(DateOnly today, IConfiguration config)
    {
        if (DateOnly.TryParse(config["Scheduling:SchoolYearEnd"], out var configured) && configured >= today)
            return configured;
        var year = today.Month >= 7 ? today.Year + 1 : today.Year;
        return new DateOnly(year, 6, 30);
    }

    public static async Task<Result> ExtendAllAsync(
        AbderaDbContext db, IClock clock, IConfiguration config, INotificationScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var from = today.AddDays(1);
        var until = SchoolYearEnd(today, config);

        var candidates = await db.LessonSeries
            .Where(s => s.Status == LessonSeriesStatus.Active)
            .Where(s => s.EffectiveUntil == null || s.EffectiveUntil >= from)
            .Join(db.Enrollments.Where(e => e.Status == EnrollmentStatus.Active),
                s => s.EnrollmentId, e => e.Id, (s, e) => new { Series = s, Enrollment = e })
            .Join(db.Students.Where(st => st.Status == StudentStatus.Active),
                x => x.Enrollment.StudentId, st => st.Id, (x, _) => x)
            .OrderBy(x => x.Series.CreatedAt)
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var summary = await LessonSeriesFeatures.GenerateForSeriesAsync(
                candidate.Series, candidate.Enrollment, db, clock, config, scheduler, from, until);
            created += summary.Created;
        }

        return new Result(candidates.Count, created, until);
    }
}
