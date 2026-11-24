using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// docs/10-decisions.md AM: seri kurulurken yalnızca GenerationWeeks haftalık ders üretiliyordu ve
// pencereyi ileri taşıyan bir iş yoktu - Eylül sonunda girilen program Kasım sonunda takvimden
// boşaldı. Artık her seri bugünden HorizonMonths (varsayılan 12) ay ileriye kadar yazılı tutulur:
// seri kurulurken/taşınırken hemen, sonra bu iş her gün pencereyi bir gün daha ileri kaydırır -
// "bitiş" diye bir tarih yoktur, seri bitirilene kadar takvim dolu kalır. Yalnızca EKSİK günleri açar:
// var olan derse (iptal, taşınmış, telafi zinciri dahil) dokunmaz; tatil ve öğretmen izni
// atlanır; bitmiş/donmuş kayıt ve pasif öğrenci uzatılmaz. Bugünün dersi açılmaz (yarından başlar).
public static class SeriesExtension
{
    public record Result(int SeriesVisited, int LessonsCreated, DateOnly Until);

    // Ufkun tek hesabı: bugünden Scheduling:HorizonMonths ay sonrası (kayan pencere, AM1). Yaz
    // ayları dahil - okul yazın da ders veriyor; kapalı günler tatil olarak girilir.
    public static DateOnly HorizonEnd(DateOnly today, IConfiguration config)
    {
        var months = config.GetValue("Scheduling:HorizonMonths", 12);
        return today.AddMonths(Math.Clamp(months, 1, 24));
    }

    public static async Task<Result> ExtendAllAsync(
        AbderaDbContext db, IClock clock, IConfiguration config, INotificationScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var from = today.AddDays(1);
        var until = HorizonEnd(today, config);

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
