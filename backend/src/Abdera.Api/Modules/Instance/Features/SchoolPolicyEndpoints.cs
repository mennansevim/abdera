using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Instance.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Instance.Features;

// Okulun kuralları (docs/multi-tenant/ARCHITECTURE.md §5.3). Personel okur (öğretmenin takvimi
// okul günü saatlerini kullanır), yalnızca yönetici değiştirir. Değişiklik geçmiş satırları
// etkilemez: aidat ve ödeme satırları hesabı zaten donduruyor.
public static class SchoolPolicyEndpoints
{
    public record PolicyResponse(
        decimal GroupLessonSharePercent, int MaxWeeklyLessons, int MakeupNoticeHours, int MakeupCreditValidDays,
        int ExcusedMakeupWindowDays, TimeOnly SchoolDayStart, TimeOnly SchoolDayEnd);

    public record UpdateRequest(
        decimal GroupLessonSharePercent, int MaxWeeklyLessons, int MakeupNoticeHours, int MakeupCreditValidDays,
        int ExcusedMakeupWindowDays, TimeOnly SchoolDayStart, TimeOnly SchoolDayEnd);

    public static void MapSchoolPolicy(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/instance/policy", GetAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPut("/api/instance/policy", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> GetAsync(AbderaDbContext db, IConfiguration config) =>
        Results.Ok(ToResponse(await SchoolPolicy.GetCurrentAsync(db, config)));

    private static async Task<IResult> UpdateAsync(
        UpdateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IConfiguration config, IClock clock)
    {
        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var policy = await db.SchoolPolicies.SingleOrDefaultAsync(p => p.Id == SchoolPolicy.SingletonId);
        if (policy is null)
        {
            policy = SchoolPolicy.CreateDefault(config, now);
            db.SchoolPolicies.Add(policy);
        }

        var before = JsonSerializer.Serialize(ToResponse(policy));
        policy.Update(
            request.GroupLessonSharePercent, request.MaxWeeklyLessons, request.MakeupNoticeHours,
            request.MakeupCreditValidDays, request.ExcusedMakeupWindowDays, request.SchoolDayStart, request.SchoolDayEnd,
            actorId, now);

        // Grup dersi payı para hesabını, telafi süreleri takvimi değiştirir: CLAUDE.md gereği audit'li.
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "school_policy.updated", nameof(SchoolPolicy), policy.Id, now,
            beforeJson: before, afterJson: JsonSerializer.Serialize(ToResponse(policy))));
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(policy));
    }

    public static PolicyResponse ToResponse(SchoolPolicy policy) => new(
        policy.GroupLessonSharePercent, policy.MaxWeeklyLessons, policy.MakeupNoticeHours, policy.MakeupCreditValidDays,
        policy.ExcusedMakeupWindowDays, policy.SchoolDayStart, policy.SchoolDayEnd);
}
