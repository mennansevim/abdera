using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// docs/07-api.md /api/school-calendar-days. Resmi tatiller ders üretiminden düşer; okul
// etkinlikleri (resital vb.) yalnızca bilgilendirme amaçlı (docs/10-decisions.md C5).
public static class SchoolCalendarDays
{
    public record CreateRequest(DateOnly Date, SchoolCalendarDayType Type, string Label);
    public record DayResponse(Guid Id, DateOnly Date, SchoolCalendarDayType Type, string Label);

    public static void MapSchoolCalendarDays(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/school-calendar-days").RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(DateOnly? from, DateOnly? to, AbderaDbContext db)
    {
        var query = db.SchoolCalendarDays.AsQueryable();
        if (from is { } f) query = query.Where(d => d.Date >= f);
        if (to is { } t) query = query.Where(d => d.Date <= t);

        var days = await query
            .OrderBy(d => d.Date)
            .Select(d => new DayResponse(d.Id, d.Date, d.Type, d.Label))
            .ToListAsync();

        return Results.Ok(days);
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, AbderaDbContext db)
    {
        if (await db.SchoolCalendarDays.AnyAsync(d => d.Date == request.Date))
            throw new ConflictException("Bu tarih için zaten bir kayıt var.");

        var day = SchoolCalendarDay.Create(request.Date, request.Type, request.Label);
        db.SchoolCalendarDays.Add(day);
        await db.SaveChangesAsync();

        return Results.Created($"/api/school-calendar-days/{day.Id}", new DayResponse(day.Id, day.Date, day.Type, day.Label));
    }
}
