using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Banking.Features;

// docs/12-bank-integration.md "Geliştirme ortamı - gerçek sağlayıcı hesabı olmadan":
// eşleştirme mantığı gerçek sağlayıcı seçilmeden önce uçtan uca test edilebilsin diye.
// Yalnızca Development ortamında haritalanır (bkz. BankingModule.MapBankingModule).
public static class DevBankSimulator
{
    public record SimulateTransactionRequest(
        string VirtualIban, decimal Amount, string? Currency, string? SenderName, string? Description, string? ProviderTransactionId);

    public static void MapDevBankSimulator(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/bank/simulate-transaction", SimulateAsync).AllowAnonymous();
    }

    private static async Task<IResult> SimulateAsync(SimulateTransactionRequest request, AbderaDbContext db, IClock clock)
    {
        var providerTransactionId = request.ProviderTransactionId ?? $"sim-{Guid.NewGuid()}";

        await Webhooks.ProcessIncomingTransactionAsync(
            "Fake", providerTransactionId, request.VirtualIban, request.Amount, request.Currency ?? "TRY",
            request.SenderName, request.Description, clock.UtcNow, db, clock);

        return Results.Ok();
    }
}
