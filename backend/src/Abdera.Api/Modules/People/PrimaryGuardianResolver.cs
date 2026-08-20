using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People;

// docs/03-erd.md notification_jobs tablosunda guardian_id yok, yalnızca tek bir
// recipient_phone_number var - yani bir ders için bildirim tek bir veliye gider. Hangi veli:
// StudentGuardian.IsPrimary=true olan. Birden fazla veli varsa ve hiçbiri birincil
// işaretlenmemişse, deterministik olarak ilkini alır (Scheduling ve Messaging paylaşır).
public static class PrimaryGuardianResolver
{
    public static async Task<Guid?> ResolveAsync(AbderaDbContext db, Guid studentId)
    {
        var guardianId = await db.StudentGuardians
            .Where(sg => sg.StudentId == studentId)
            .OrderByDescending(sg => sg.IsPrimary)
            .ThenBy(sg => sg.GuardianId)
            .Select(sg => (Guid?)sg.GuardianId)
            .FirstOrDefaultAsync();

        return guardianId;
    }
}
