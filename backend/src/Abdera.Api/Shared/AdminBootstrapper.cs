using Abdera.Api.Modules.Auth.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Shared;

// docs/03-erd.md / .env.example - Bootstrap__AdminEmail, Bootstrap__AdminPassword.
// Yalnızca users tablosu tamamen boşsa (ilk kurulum) çalışır - var olan bir kuruluma
// tekrar admin eklemez. İlk girişte MustChangePassword=true ile şifre değişimi zorlanır.
public static class AdminBootstrapper
{
    public static async Task RunAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        if (await db.Users.AnyAsync())
        {
            return;
        }

        var email = config["Bootstrap:AdminEmail"];
        var password = config["Bootstrap:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Bootstrap__AdminEmail / Bootstrap__AdminPassword tanımlı değil - ilk yönetici oluşturulamadı. " +
                ".env dosyanı kontrol et.");
            return;
        }

        var admin = User.Create(email, "placeholder", UserRole.Admin, clock.UtcNow, mustChangePassword: true);
        var hash = passwordHasher.HashPassword(admin, password);
        admin.SetPassword(hash, clock.UtcNow, mustChangePassword: true);

        db.Users.Add(admin);
        db.AuditLogs.Add(AuditLog.Record(null, "user.bootstrap_admin_created", nameof(User), admin.Id, clock.UtcNow));
        await db.SaveChangesAsync();

        logger.LogInformation("İlk yönetici hesabı oluşturuldu: {Email}", email);
    }
}
