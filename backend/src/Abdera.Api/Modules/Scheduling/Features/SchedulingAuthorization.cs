using System.Security.Claims;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Scheduling.Features;

// Öğretmen kendi haftalık programını kurar (docs/10-decisions.md K1): uygunluk pencerelerini
// açıp kapatır ve kendi öğrencisi için ders serisi oluşturur. "Kendi" kelimesinin bu modüldeki
// tek tanımı burada: oturumdan çözülen teacherId, hedef kaynağın teacherId'siyle birebir aynı
// olmalı - URL'deki ya da istek gövdesindeki id'ye asla güvenilmez (docs/04-permissions.md).
//
// People modülündeki PeopleAuthorization.EnsureActsAsSelfAsync ile aynı desen; kopyası kalıyor
// çünkü modüller arası doğrudan bağımlılık kurmuyoruz (CLAUDE.md modül sınırı kuralı).
internal static class SchedulingAuthorization
{
    // Admin için null döner (kapsam yok - okulun tamamını yönetir), öğretmen için kendi
    // teacherId'si. Çağıran, dönen değerin null olup olmamasına göre ek kapsam kontrolü yapar.
    public static async Task<Guid?> EnsureActsAsSelfAsync(
        Guid teacherId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var scopedTeacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (scopedTeacherId is null) return null;

        if (scopedTeacherId != teacherId)
            throw new ForbiddenException("Yalnızca kendi ders programınızı düzenleyebilirsiniz.");

        return scopedTeacherId;
    }
}
