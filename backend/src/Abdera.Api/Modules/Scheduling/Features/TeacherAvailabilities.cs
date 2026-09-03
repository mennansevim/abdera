using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// docs/07-api.md GET /api/teachers/{teacherId}/availability. Uygunluk tanımlamak Admin
// işi; öğretmen kendi uygunluğunu görebilir ama değiştiremez (talep üzerinden gider - Phase 3).
public static class TeacherAvailabilities
{
    public record CreateRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
    public record AvailabilityResponse(Guid Id, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

    public static void MapTeacherAvailabilities(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/teachers/{teacherId:guid}/availability", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapPost("/api/teachers/{teacherId:guid}/availability", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        // Öğretmenler sayfasındaki "uygun günler" tek-tık aç/kapa arayüzü bunu kullanır -
        // bir günü kapatmak, o güne ait uygunluk penceresini kaldırmaktır. Finansal/audit
        // kaydı değil (CLAUDE.md'nin "silinmez" kuralı yalnızca para/takvim/rıza değiştiren
        // ve denetim izi gerektiren kayıtlar için) - bu yalnızca bir zamanlama tercihi,
        // gerçek silme burada uygun.
        app.MapDelete("/api/teachers/{teacherId:guid}/availability/{availabilityId:guid}", DeleteAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(Guid teacherId, AbderaDbContext db)
    {
        var items = await db.TeacherAvailabilities
            .Where(a => a.TeacherId == teacherId)
            .OrderBy(a => a.DayOfWeek).ThenBy(a => a.StartTime)
            .Select(a => new AvailabilityResponse(a.Id, a.DayOfWeek, a.StartTime, a.EndTime))
            .ToListAsync();

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(Guid teacherId, CreateRequest request, AbderaDbContext db)
    {
        if (!await db.Teachers.AnyAsync(t => t.Id == teacherId))
            throw new NotFoundException("Öğretmen bulunamadı.");

        var availability = TeacherAvailability.Create(teacherId, request.DayOfWeek, request.StartTime, request.EndTime);
        db.TeacherAvailabilities.Add(availability);
        await db.SaveChangesAsync();

        return Results.Created(
            $"/api/teachers/{teacherId}/availability/{availability.Id}",
            new AvailabilityResponse(availability.Id, availability.DayOfWeek, availability.StartTime, availability.EndTime));
    }

    private static async Task<IResult> DeleteAsync(Guid teacherId, Guid availabilityId, AbderaDbContext db)
    {
        var availability = await db.TeacherAvailabilities
            .SingleOrDefaultAsync(a => a.Id == availabilityId && a.TeacherId == teacherId)
            ?? throw new NotFoundException("Uygunluk kaydı bulunamadı.");

        db.TeacherAvailabilities.Remove(availability);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }
}
