using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Infrastructure;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Tests.Integration;

// Aidatın günlük bakımı (MonthlyReceivableGenerator + OverdueReceivableSweeper'ın ortak işi):
// kayıt açılırken tarife eksik olduğu için aidatı açılmamış aktif kayıt ("Aidat açılmadı"),
// tarife gelince bir sonraki turda açılır. Ayrı sınıf (ayrı Postgres), çünkü Grup tarifesiyle
// oynuyor ve bu ayın eksik aidatlarını toplu açıyor.
public class DailyBillingJobFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public DailyBillingJobFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<BillingDailyJob.Result> RunDailyJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await BillingDailyJob.RunAsync(
            scope.ServiceProvider.GetRequiredService<AbderaDbContext>(), scope.ServiceProvider.GetRequiredService<IClock>());
    }

    [Fact]
    public async Task Daily_job_opens_the_missing_receivable_for_this_month_once()
    {
        var admin = _factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();
        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        var art = instruments!.Single(i => i.Code == "ART").Id;
        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest("Gunluk", "Ogretmen", [art], null))).Content
            .ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
            new Students.CreateRequest("Gunluk", "Ogrenci", new DateOnly(2014, 1, 1)))).Content
            .ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var period = TestPeriods.Current(_factory.Services);

        // Kayıt açılırken bu ayı kapsayan Grup tarifesi yok -> aidat açılmaz (canlıdaki
        // "Aidat açılmadı" durumu). Sonra tarife geri gelir; aidatı açacak olan günlük iştir.
        await using var db = await _factory.CreateDbContextAsync();
        var groupRates = await db.TuitionRates.AsNoTracking()
            .Where(r => r.CourseKind == CourseKind.Group)
            .Select(r => new { r.Id, r.EffectiveFrom })
            .ToListAsync();
        await db.TuitionRates.Where(r => r.CourseKind == CourseKind.Group && r.EffectiveUntil == null)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.EffectiveFrom, BillingPeriod.FirstDay(period).AddMonths(1)));
        Enrollments.EnrollmentResponse enrollment;
        try
        {
            var created = await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, art, new DateOnly(2026, 9, 1), CourseKind.Group));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            enrollment = (await created.Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
            Assert.False(await db.Receivables.AnyAsync(r => r.EnrollmentId == enrollment.Id));
        }
        finally
        {
            foreach (var rate in groupRates)
            {
                await db.TuitionRates.Where(r => r.Id == rate.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(r => r.EffectiveFrom, rate.EffectiveFrom));
            }
        }

        var first = await RunDailyJobAsync();
        Assert.True(first.CreatedCount >= 1);

        var receivable = await db.Receivables.AsNoTracking().SingleAsync(r => r.EnrollmentId == enrollment.Id && r.Period == period);
        // Vadesi geçmişse aynı çalışma onu Overdue'ya da çevirir (ödeme bekliyor -> gecikti).
        var clock = _factory.Services.GetRequiredService<IClock>();
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        Assert.Equal(receivable.DueDate < today ? ReceivableStatus.Overdue : ReceivableStatus.Unpaid, receivable.Status);
        Assert.True(receivable.Amount > 0);

        // İdempotent: ikinci çalışma yeni satır açmaz.
        var second = await RunDailyJobAsync();
        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(1, await db.Receivables.CountAsync(r => r.EnrollmentId == enrollment.Id && r.Period == period));
    }
}
