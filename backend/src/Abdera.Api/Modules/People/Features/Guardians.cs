using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// docs/07-api.md /api/guardians. docs/04-permissions.md: veli bilgisi (telefon, KVKK
// kapsamındaki veri) yalnızca Admin'e açık - öğretmenin veli iletişim bilgisine erişimi yok.
public static class Guardians
{
    public record CreateRequest(string FirstName, string LastName, string PhoneNumber);
    public record UpdateRequest(string FirstName, string LastName, string PhoneNumber);
    public record GuardianResponse(Guid Id, string FirstName, string LastName, string PhoneNumber, bool NotificationConsent);

    public static void MapGuardians(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/guardians").RequireAuthorization(AuthorizationPolicies.AdminOnly);

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPatch("/{guardianId:guid}", UpdateAsync);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var guardians = await db.Guardians
            .OrderBy(g => g.LastName).ThenBy(g => g.FirstName)
            .Select(g => new GuardianResponse(g.Id, g.FirstName, g.LastName, g.PhoneNumber, g.NotificationConsent))
            .ToListAsync();

        return Results.Ok(guardians);
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, AbderaDbContext db, IClock clock)
    {
        var normalizedPhone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
        if (await db.Guardians.AnyAsync(g => g.PhoneNumber == normalizedPhone))
        {
            throw new ConflictException("Bu telefon numarasıyla kayıtlı bir veli zaten var.");
        }

        var guardian = Guardian.Create(request.FirstName, request.LastName, request.PhoneNumber, clock.UtcNow);
        db.Guardians.Add(guardian);
        await db.SaveChangesAsync();

        return Results.Created($"/api/guardians/{guardian.Id}",
            new GuardianResponse(guardian.Id, guardian.FirstName, guardian.LastName, guardian.PhoneNumber, guardian.NotificationConsent));
    }

    private static async Task<IResult> UpdateAsync(Guid guardianId, UpdateRequest request, AbderaDbContext db, IClock clock)
    {
        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.Id == guardianId)
            ?? throw new NotFoundException("Veli bulunamadı.");

        var normalizedPhone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
        if (normalizedPhone != guardian.PhoneNumber && await db.Guardians.AnyAsync(g => g.PhoneNumber == normalizedPhone))
        {
            throw new ConflictException("Bu telefon numarasıyla kayıtlı başka bir veli var.");
        }

        guardian.Update(request.FirstName, request.LastName, request.PhoneNumber, clock.UtcNow);
        await db.SaveChangesAsync();

        return Results.Ok(new GuardianResponse(guardian.Id, guardian.FirstName, guardian.LastName, guardian.PhoneNumber, guardian.NotificationConsent));
    }
}
