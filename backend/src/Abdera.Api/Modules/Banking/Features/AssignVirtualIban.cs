using Abdera.Api.Modules.Banking.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Banking.Features;

// docs/12-bank-integration.md "Sanal IBAN ataması". Admin bir veliye sanal IBAN atar -
// IBankPaymentProvider (Fake veya gerçek sağlayıcı) IBAN'ı tahsis eder, biz sonucu saklarız.
public static class AssignVirtualIban
{
    public record VirtualIbanResponse(Guid Id, Guid GuardianId, string Iban, string Provider, VirtualIbanStatus Status);

    public static void MapAssignVirtualIban(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/guardians/{guardianId:guid}/virtual-iban", AssignAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapGet("/api/guardians/{guardianId:guid}/virtual-iban", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> AssignAsync(
        Guid guardianId, AbderaDbContext db, IClock clock, IBankPaymentProvider provider)
    {
        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.Id == guardianId)
            ?? throw new NotFoundException("Veli bulunamadı.");

        var alreadyActive = await db.VirtualIbans.AnyAsync(v => v.GuardianId == guardianId && v.Status == VirtualIbanStatus.Active);
        if (alreadyActive)
            throw new ConflictException("Bu veliye zaten aktif bir sanal IBAN atanmış.");

        var allocation = await provider.AllocateVirtualIbanAsync(guardianId);
        if (!allocation.Success || allocation.Iban is null)
            throw new ConflictException(allocation.Error ?? "Sanal IBAN tahsis edilemedi.");

        var virtualIban = VirtualIban.Create(guardianId, allocation.Iban, allocation.Provider, allocation.ProviderReference, clock.UtcNow);
        db.VirtualIbans.Add(virtualIban);
        await db.SaveChangesAsync();

        return Results.Created($"/api/guardians/{guardianId}/virtual-iban",
            new VirtualIbanResponse(virtualIban.Id, virtualIban.GuardianId, virtualIban.Iban, virtualIban.Provider, virtualIban.Status));
    }

    private static async Task<IResult> GetAsync(Guid guardianId, AbderaDbContext db)
    {
        var virtualIban = await db.VirtualIbans
            .Where(v => v.GuardianId == guardianId && v.Status == VirtualIbanStatus.Active)
            .SingleOrDefaultAsync();

        if (virtualIban is null) return Results.NotFound();

        return Results.Ok(new VirtualIbanResponse(virtualIban.Id, virtualIban.GuardianId, virtualIban.Iban, virtualIban.Provider, virtualIban.Status));
    }
}
