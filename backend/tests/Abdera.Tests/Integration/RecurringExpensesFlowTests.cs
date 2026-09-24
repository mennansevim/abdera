using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Sabit aylık gider kalemleri HTTP üzerinden: tutar değişikliği yeni sürüm satırı açar, eski
// sürüm silinmeden kapanır; kısmi unique index (kalem başına tek açık tutar) gerçek Postgres'te.
public class RecurringExpensesFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public RecurringExpensesFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"));
        response.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Rent_change_opens_a_new_version_from_the_chosen_month_and_keeps_the_old_one()
    {
        var admin = await CreateAdminClientAsync();

        var createResponse = await admin.PostAsJsonAsync("/api/recurring-expenses", new RecurringExpenses.CreateRequest(
            ExpenseCategory.Rent, "Kira - test", 20000m, "2026-01", null, null));
        var body = await createResponse.Content.ReadAsStringAsync();
        Assert.True(createResponse.StatusCode == HttpStatusCode.Created, $"Beklenmeyen durum: {createResponse.StatusCode}, gövde: {body}");
        var created = (await createResponse.Content.ReadFromJsonAsync<RecurringExpenses.RecurringExpenseResponse>(TestJson.Options))!;

        var changeResponse = await admin.PostAsJsonAsync($"/api/recurring-expenses/{created.Id}/amounts",
            new RecurringExpenses.ChangeAmountRequest(25000m, "2026-07", null));
        var changeBody = await changeResponse.Content.ReadAsStringAsync();
        Assert.True(changeResponse.StatusCode == HttpStatusCode.OK, $"Beklenmeyen durum: {changeResponse.StatusCode}, gövde: {changeBody}");

        var list = await admin.GetFromJsonAsync<List<RecurringExpenses.RecurringExpenseResponse>>("/api/recurring-expenses", TestJson.Options);
        var rent = list!.Single(item => item.Id == created.Id);
        Assert.Collection(rent.Amounts,
            first => { Assert.Equal(20000m, first.MonthlyAmount); Assert.Equal("2026-01", first.EffectiveFrom); Assert.Equal("2026-06", first.EffectiveUntil); },
            second => { Assert.Equal(25000m, second.MonthlyAmount); Assert.Equal("2026-07", second.EffectiveFrom); Assert.Null(second.EffectiveUntil); });

        await using var db = await _factory.CreateDbContextAsync();
        Assert.Equal(2, await db.RecurringExpenseAmounts.CountAsync(amount => amount.RecurringExpenseId == created.Id));
        Assert.Equal(2, await db.AuditLogs.CountAsync(log => log.EntityId == created.Id));
    }

    [Fact]
    public async Task Change_before_the_current_amount_started_is_rejected_and_nothing_is_written()
    {
        var admin = await CreateAdminClientAsync();
        var created = (await (await admin.PostAsJsonAsync("/api/recurring-expenses", new RecurringExpenses.CreateRequest(
            ExpenseCategory.Utilities, "Elektrik/su ortalaması", 3000m, "2026-06", null, null)))
            .Content.ReadFromJsonAsync<RecurringExpenses.RecurringExpenseResponse>(TestJson.Options))!;

        var response = await admin.PostAsJsonAsync($"/api/recurring-expenses/{created.Id}/amounts",
            new RecurringExpenses.ChangeAmountRequest(3500m, "2026-05", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = await _factory.CreateDbContextAsync();
        Assert.Equal(1, await db.RecurringExpenseAmounts.CountAsync(amount => amount.RecurringExpenseId == created.Id));
    }

    [Fact]
    public async Task Invalid_month_is_rejected()
    {
        var admin = await CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/recurring-expenses", new RecurringExpenses.CreateRequest(
            ExpenseCategory.Rent, "Kira", 20000m, "2026-13", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
