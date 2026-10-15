using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Faz 2: Maliyet Takibi'nin gider defteri. CLAUDE.md - finansal kayıt silinmez, negatif/sıfır
// tutar hem uygulama katmanında hem veritabanı CHECK kısıtıyla (CK_expenses_amount) reddedilir.
public class ExpensesFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public ExpensesFlowTests(AbderaWebApplicationFactory factory)
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
    public async Task Admin_can_record_and_list_expenses()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var createResponse = await admin.PostAsJsonAsync("/api/expenses", new Expenses.CreateRequest(
            ExpenseCategory.Rent, "Ağustos kira", 15000m, "TRY", new DateOnly(2026, 8, 1), null));
        var body = await createResponse.Content.ReadAsStringAsync();
        Assert.True(createResponse.StatusCode == HttpStatusCode.Created, $"Beklenmeyen durum: {createResponse.StatusCode}, gövde: {body}");
        var created = (await createResponse.Content.ReadFromJsonAsync<Expenses.ExpenseResponse>(TestJson.Options))!;
        Assert.Equal(15000m, created.Amount);
        Assert.Equal(ExpenseCategory.Rent, created.Category);

        // Gerçekten kalıcı tabloya yazıldığını doğrula (bu tablo daha önce bir migration
        // eksikliği yüzünden hiç oluşmuyordu - HTTP üzerinden doğrulamak bu sınıf bug'ı yakalar).
        var stored = await db.Expenses.AsNoTracking().SingleAsync(e => e.Id == created.Id);
        Assert.Equal("Ağustos kira", stored.Description);

        var listResponse = await admin.GetAsync("/api/expenses");
        var list = await listResponse.Content.ReadFromJsonAsync<List<Expenses.ExpenseResponse>>(TestJson.Options);
        Assert.Contains(list!, e => e.Id == created.Id);
    }

    // #35: kategori metin olarak varchar(20) kolona yazılır - her yeni enum değeri gerçek
    // Postgres'e yazılıp aynen geri okunabilmeli (uzun bir ad 22001 ile 500'e düşerdi).
    [Theory]
    [InlineData(ExpenseCategory.Marketing)]
    [InlineData(ExpenseCategory.Maintenance)]
    [InlineData(ExpenseCategory.TaxAndInsurance)]
    [InlineData(ExpenseCategory.Supplies)]
    [InlineData(ExpenseCategory.Equipment)]
    public async Task New_expense_categories_round_trip(ExpenseCategory category)
    {
        var admin = await CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/expenses", new Expenses.CreateRequest(
            category, $"{category} gideri", 750m, "TRY", new DateOnly(2026, 10, 1), null));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Beklenmeyen durum: {response.StatusCode}, gövde: {body}");

        await using var db = await _factory.CreateDbContextAsync();
        var created = (await response.Content.ReadFromJsonAsync<Expenses.ExpenseResponse>(TestJson.Options))!;
        var stored = await db.Expenses.AsNoTracking().SingleAsync(e => e.Id == created.Id);
        Assert.Equal(category, stored.Category);
    }

    [Fact]
    public void Every_category_name_fits_the_category_column()
    {
        Assert.All(Enum.GetNames<ExpenseCategory>(), name => Assert.True(name.Length <= 20, $"{name} 20 karakteri aşıyor"));
    }

    [Fact]
    public async Task Zero_or_negative_amount_is_rejected()
    {
        var admin = await CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/expenses", new Expenses.CreateRequest(
            ExpenseCategory.Other, "Geçersiz gider", 0m, "TRY", new DateOnly(2026, 8, 1), null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
