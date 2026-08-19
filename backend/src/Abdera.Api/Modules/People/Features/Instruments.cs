using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// docs/07-api.md: GET/POST /api/instruments. Enstrüman kişisel/mali veri değil - okuma
// her iki role de açık (form/dropdown ihtiyacı), yazma yalnızca Admin.
public static class Instruments
{
    public record CreateRequest(string Name, string Code);
    public record InstrumentResponse(Guid Id, string Name, string Code);

    public static void MapInstruments(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/instruments", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/instruments", CreateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var instruments = await db.Instruments
            .OrderBy(i => i.Name)
            .Select(i => new InstrumentResponse(i.Id, i.Name, i.Code))
            .ToListAsync();

        return Results.Ok(instruments);
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, AbderaDbContext db)
    {
        if (await db.Instruments.AnyAsync(i => i.Code == request.Code.ToUpperInvariant()))
        {
            throw new ConflictException($"'{request.Code}' kodlu bir enstrüman zaten var.");
        }

        var instrument = Instrument.Create(request.Name, request.Code);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync();

        return Results.Created($"/api/instruments/{instrument.Id}", new InstrumentResponse(instrument.Id, instrument.Name, instrument.Code));
    }
}
