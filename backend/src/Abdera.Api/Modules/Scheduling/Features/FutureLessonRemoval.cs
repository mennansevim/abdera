using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// Kurs kaydı bitince ya da öğrenci pasife alınınca gelecekteki planlı dersler takvimden kalkar
// (docs/10-decisions.md AI). Takvim iptal edilmiş dersi üstü çizili gösterdiği için ders Cancel
// edilmez, satır kaldırılır. Bu dersler öğrenci adına "doğmuş" bir telafi hakkı üretmez.
// Değişiklikler yalnızca izlenir; SaveChanges çağıranın transaction'ındadır.
internal static class FutureLessonRemoval
{
    internal record Result(Guid LessonId, Guid TeacherId, DateTimeOffset StartAt, LessonStatus PreviousStatus, bool KeptAsCancelled);

    public static async Task<List<Result>> RemoveAsync(List<Lesson> lessons, AbderaDbContext db, DateTimeOffset now)
    {
        if (lessons.Count == 0) return [];
        var lessonIds = lessons.Select(lesson => lesson.Id).ToList();

        // Ders satırı silinince FK'sız tablolarda sahipsiz satır kalır, derse bağlı bekleyen
        // hatırlatma da var olmayan bir ders için gönderilmeye çalışılırdı.
        var jobs = await db.NotificationJobs
            .Where(job => job.ReferenceType == "lesson" && lessonIds.Contains(job.ReferenceId))
            .Where(job => job.Status == NotificationJobStatus.Pending || job.Status == NotificationJobStatus.Processing)
            .ToListAsync();
        foreach (var job in jobs) job.Cancel(now);

        var rsvps = await db.LessonRsvps.Where(rsvp => lessonIds.Contains(rsvp.LessonId)).ToListAsync();
        db.LessonRsvps.RemoveRange(rsvps);

        var changeRequests = await db.LessonChangeRequests
            .Where(request => lessonIds.Contains(request.LessonId) && request.Status == LessonChangeRequestStatus.Pending)
            .ToListAsync();
        foreach (var request in changeRequests) request.Reject(now);

        // Telafi dersi yapılmayacağı için harcanan hak geri döner; süresi kendi takvimiyle işler.
        var makeupCredits = await db.MakeupCredits
            .Where(credit => credit.UsedLessonId != null && lessonIds.Contains(credit.UsedLessonId.Value))
            .ToListAsync();
        foreach (var credit in makeupCredits) credit.ReleaseLesson();

        // Nadir: ileri tarihli derse not/ödev/kayıt bağlanmışsa o içerik korunur, ders iptal edilir.
        var withContent = await LoadLessonsWithContentAsync(lessonIds, db);

        var results = new List<Result>(lessons.Count);
        foreach (var lesson in lessons)
        {
            var keep = withContent.Contains(lesson.Id);
            results.Add(new Result(lesson.Id, lesson.TeacherId, lesson.StartAt, lesson.Status, keep));
            if (keep) lesson.Cancel(now);
            else db.Lessons.Remove(lesson);
        }

        return results;
    }

    private static async Task<HashSet<Guid>> LoadLessonsWithContentAsync(List<Guid> lessonIds, AbderaDbContext db)
    {
        var withContent = await db.LessonNotes
            .Where(note => lessonIds.Contains(note.LessonId))
            .Select(note => note.LessonId)
            .ToHashSetAsync();
        withContent.UnionWith(await db.PracticeAssignments
            .Where(item => lessonIds.Contains(item.LessonId))
            .Select(item => item.LessonId)
            .ToListAsync());
        withContent.UnionWith(await db.SkillAssessments
            .Where(item => item.LessonId != null && lessonIds.Contains(item.LessonId.Value))
            .Select(item => item.LessonId!.Value)
            .ToListAsync());
        withContent.UnionWith(await db.AudioClips
            .Where(item => item.LessonId != null && lessonIds.Contains(item.LessonId.Value))
            .Select(item => item.LessonId!.Value)
            .ToListAsync());
        return withContent;
    }
}
