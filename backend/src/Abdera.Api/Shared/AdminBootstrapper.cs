using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.People.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Shared;

// docs/03-erd.md / .env.example - Bootstrap__AdminEmail, Bootstrap__AdminPassword.
// Yalnızca users tablosu tamamen boşsa (ilk kurulum) admin oluşturur. Development
// ortamında öğretmen önizlemesini kolaylaştırmak için demo öğretmen hesabını da
// idempotent biçimde hazırlar; production ortamında öğretmen bootstrap'i çalışmaz.
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

        if (!await db.Users.AnyAsync())
        {
            var email = config["Bootstrap:AdminEmail"];
            var password = config["Bootstrap:AdminPassword"];

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning(
                    "Bootstrap__AdminEmail / Bootstrap__AdminPassword tanımlı değil - ilk yönetici oluşturulamadı. " +
                    ".env dosyanı kontrol et.");
            }
            else
            {
                var admin = User.Create(email, "placeholder", UserRole.Admin, clock.UtcNow, mustChangePassword: true);
                var hash = passwordHasher.HashPassword(admin, password);
                admin.SetPassword(hash, clock.UtcNow, mustChangePassword: true);

                db.Users.Add(admin);
                db.AuditLogs.Add(AuditLog.Record(null, "user.bootstrap_admin_created", nameof(User), admin.Id, clock.UtcNow));
                await db.SaveChangesAsync();

                logger.LogInformation("İlk yönetici hesabı oluşturuldu: {Email}", email);
            }
        }

        if (app.Environment.IsDevelopment())
        {
            await EnsureDevelopmentTeacherAsync(db, passwordHasher, clock, config, logger);
        }
    }

    private static async Task EnsureDevelopmentTeacherAsync(
        AbderaDbContext db,
        IPasswordHasher<User> passwordHasher,
        IClock clock,
        IConfiguration config,
        ILogger<Program> logger)
    {
        var email = (config["Bootstrap:TeacherEmail"] ?? "teacher@example.com").Trim().ToLowerInvariant();
        var password = config["Bootstrap:TeacherPassword"] ?? "DevTeacher123!";

        // Yalnızca öğretmen tablosu tamamen boşsa (gerçekten ilk kurulum) demo öğretmen
        // eklenir. Önceden yalnızca bu e-postanın varlığına bakılıyordu; bu da admin
        // gerçek bir öğretmen ekleyip demo hesabını sildikten sonra her API yeniden
        // başlatmasında (docker compose restart/up) "Demo Öğretmen"i sessizce geri
        // getiriyordu - gerçek bir bug olarak bulundu (demo veri seti kürlerken).
        if (await db.Teachers.AnyAsync())
        {
            return;
        }

        var instrument = await db.Instruments
            .Where(item => item.Code == "PIANO")
            .FirstOrDefaultAsync()
            ?? await db.Instruments.OrderBy(item => item.Code).FirstOrDefaultAsync();

        if (instrument is null)
        {
            logger.LogWarning("Demo öğretmen oluşturulamadı: henüz enstrüman seed edilmemiş.");
            return;
        }

        var teacherUser = User.Create(email, "placeholder", UserRole.Teacher, clock.UtcNow);
        teacherUser.SetPassword(passwordHasher.HashPassword(teacherUser, password), clock.UtcNow);
        var teacher = Teacher.Create("Demo", "Öğretmen", clock.UtcNow, teacherUser.Id);

        db.Users.Add(teacherUser);
        db.Teachers.Add(teacher);
        db.TeacherInstruments.Add(TeacherInstrument.Create(teacher.Id, instrument.Id));
        db.AuditLogs.Add(AuditLog.Record(null, "user.bootstrap_development_teacher_created", nameof(User), teacherUser.Id, clock.UtcNow));
        await db.SaveChangesAsync();

        logger.LogInformation("Development demo öğretmen hesabı oluşturuldu: {Email}", email);
    }
}
