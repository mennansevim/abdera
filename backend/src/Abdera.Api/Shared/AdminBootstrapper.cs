using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.People.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Shared;

// docs/03-erd.md / .env.example - Bootstrap__AdminEmail, Bootstrap__AdminPassword.
// Yalnızca users tablosu tamamen boşsa (ilk kurulum) admin oluşturur. Development
// ortamında öğretmen önizlemesini kolaylaştırmak için demo öğretmen hesabını da
// idempotent biçimde hazırlar. Demo:Enabled açıldığında staging yayını için ayrı,
// herkese açık demo yönetici/öğretmen hesapları hazırlanır.
public static class AdminBootstrapper
{
    private const string DemoAdminEmail = "demo.yonetici@abdera.com";
    private const string DemoTeacherEmail = "demo.ogretmen@abdera.com";
    private const string DemoPassword = "AbderaDemo2026!";

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

        if (config.GetValue<bool>("Demo:Enabled"))
        {
            await EnsureDemoAccountsAsync(db, passwordHasher, clock, logger);
        }
    }

    private static async Task EnsureDemoAccountsAsync(
        AbderaDbContext db,
        IPasswordHasher<User> passwordHasher,
        IClock clock,
        ILogger<Program> logger)
    {
        var admin = await db.Users.SingleOrDefaultAsync(item => item.Email == DemoAdminEmail);
        // YENİ oluşturulan hesabın PasswordHash'i "placeholder" - geçerli bir hash değil.
        // VerifyHashedPassword onu Base64 olarak çözmeye çalışıp FormatException fırlatıyor
        // ve uygulama açılışta çöküyordu (boş veritabanı + Demo:Enabled=true). Mevcut bir
        // veritabanında hesaplar zaten gerçek hash taşıdığı için hata yalnızca İLK kurulumda
        // görünüyordu; bu yüzden gözden kaçmıştı.
        var adminIsNew = admin is null;
        if (admin is null)
        {
            admin = User.Create(DemoAdminEmail, "placeholder", UserRole.Admin, clock.UtcNow);
            db.Users.Add(admin);
            db.AuditLogs.Add(AuditLog.Record(null, "user.bootstrap_demo_admin_created", nameof(User), admin.Id, clock.UtcNow));
        }
        // SetPassword SecurityStamp'i yeniler. Bunu her serverless cold start'ta
        // çağırmak açık admin çerezlerini geçersiz kılar ve kullanıcı takvimi görse bile
        // ders oluştururken 401 alır. Demo şifre gerçekten değişmişse (veya hesap yeni
        // oluşturulmuşsa) yalnızca o durumda yeniden hash'le.
        if (adminIsNew || passwordHasher.VerifyHashedPassword(admin, admin.PasswordHash, DemoPassword) == PasswordVerificationResult.Failed)
        {
            admin.SetPassword(passwordHasher.HashPassword(admin, DemoPassword), clock.UtcNow);
        }

        var teacherUser = await db.Users.SingleOrDefaultAsync(item => item.Email == DemoTeacherEmail);
        var teacherIsNew = teacherUser is null;
        if (teacherUser is null)
        {
            teacherUser = User.Create(DemoTeacherEmail, "placeholder", UserRole.Teacher, clock.UtcNow);
            db.Users.Add(teacherUser);
            db.AuditLogs.Add(AuditLog.Record(null, "user.bootstrap_demo_teacher_created", nameof(User), teacherUser.Id, clock.UtcNow));
        }
        if (teacherIsNew || passwordHasher.VerifyHashedPassword(teacherUser, teacherUser.PasswordHash, DemoPassword) == PasswordVerificationResult.Failed)
        {
            teacherUser.SetPassword(passwordHasher.HashPassword(teacherUser, DemoPassword), clock.UtcNow);
        }

        var teacher = await db.Teachers.SingleOrDefaultAsync(item => item.UserId == teacherUser.Id);
        if (teacher is null)
        {
            teacher = Teacher.Create("Demo", "Öğretmen", clock.UtcNow, teacherUser.Id);
            db.Teachers.Add(teacher);

            var instrument = await db.Instruments
                .Where(item => item.Code == "PIANO")
                .FirstOrDefaultAsync()
                ?? await db.Instruments.OrderBy(item => item.Code).FirstOrDefaultAsync();
            if (instrument is not null)
            {
                db.TeacherInstruments.Add(TeacherInstrument.Create(teacher.Id, instrument.Id));
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Staging demo hesapları kullanıma hazırlandı.");
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
