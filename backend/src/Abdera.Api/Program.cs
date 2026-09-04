using Abdera.Api.Modules.Attendance;
using Abdera.Api.Modules.Auth;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Banking;
using Abdera.Api.Modules.Banking.Domain;
using Abdera.Api.Modules.Banking.Infrastructure;
using Abdera.Api.Modules.Billing;
using Abdera.Api.Modules.Dashboard;
using Abdera.Api.Modules.Messaging;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Infrastructure;
using Abdera.Api.Modules.Ops;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Infrastructure;
using Abdera.Api.Modules.People;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Pricing;
using Abdera.Api.Modules.Progress;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Progress.Infrastructure;
using Abdera.Api.Modules.Scheduling;
using Abdera.Api.Shared;
using HealthChecks.NpgSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Globalization;
using System.Text.Json.Serialization;

// Sunucunun (veya konteynerin) işletim sistemi kültürü ne olursa olsun (örn. tr-TR),
// ToString()/string interpolation kültüre bağımlı biçim üretmesin - aksi halde bir decimal'i
// elle JSON'a basan kod (örn. audit log) virgüllü ondalık üretip jsonb kolonunda parse
// hatasına yol açabilir (gerçek bir prod bug'ı - bkz. BulkUpdate.cs/Payments.cs yorumları).
// Kullanıcıya gösterilecek tr-TR'ye özgü biçimlendirme (tarih/para gösterimi) gerektiğinde
// bu, ilgili yerde CultureInfo.GetCultureInfo("tr-TR") ile açıkça belirtilir.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Enum'lar JSON'da sayı değil isim olarak taşınır (örn. UserRole.Admin -> "Admin", 0 değil) -
// veritabanındaki string kolonla (users.role) ve frontend'in beklediği sözleşmeyle tutarlı.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// --- Serilog: yapılandırılmış log, CLAUDE.md "safe logging" gereği erişim jetonu/parola loglanmaz ---
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

// Bağlantı dizesi builder.Build()'den SONRA, DI çözümlenirken okunur - burada eager
// okumuyoruz çünkü WebApplicationFactory'nin (test) konfigürasyon override'ı yalnızca
// Build() sırasında devreye girer; en üst seviyede senkron okuma testleri kırar.
static string ResolveConnectionString(IServiceProvider sp) =>
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default tanımlı değil - .env dosyanı kontrol et.");

builder.Services.AddDbContext<AbderaDbContext>((sp, options) => options.UseNpgsql(ResolveConnectionString(sp)));

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
// docs/10-decisions.md Karar F reversal - veli OTP girişi için ayrı bir hasher (GuardianAuth.cs).
builder.Services.AddSingleton<IPasswordHasher<Guardian>, PasswordHasher<Guardian>>();
builder.Services.AddBillingModule();
builder.Services.AddMessagingModule();
builder.Services.AddOpsModule();

// --- Data Protection anahtarları kalıcı bir dizine yazılır ---
// Aksi halde anahtarlar yalnızca bellekte tutulur ve her container yeniden başlatmasında
// (deploy, restart) tüm oturum çerezleri sessizce geçersiz kalır - kullanıcılar habersiz
// şekilde çıkışa zorlanır. Auth__KeysDirectory docker-compose'da kalıcı bir volume'e işaret eder.
var keysDirectory = builder.Configuration["Auth:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(keysDirectory))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
        .SetApplicationName("Abdera");
}

// --- WhatsApp sağlayıcısı: env'den seçilir, kod içinde hardcode edilmez (CLAUDE.md) ---
// Not: hangi somut sınıfın DI'a kaydedileceği (Fake/Cloud) yapısal bir karardır ve
// Build()'den önce verilmek zorunda - bu yüzden burada istisnai olarak eager okunuyor.
// Gerçek ortam değişkenleri (appsettings/.env) bunu doğru görür; yalnızca
// WebApplicationFactory'nin test-time ConfigureAppConfiguration overlay'i bu satıra
// yetişemez (bkz. yukarıdaki ResolveConnectionString notu) - testlerde varsayılan
// zaten Fake olduğu için bu bir sorun yaratmıyor.
builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));
var whatsAppProvider = builder.Configuration["WhatsApp:Provider"] ?? "Fake";
if (string.Equals(whatsAppProvider, "Cloud", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IWhatsAppClient, CloudApiWhatsAppClient>();
}
else
{
    builder.Services.AddSingleton<IWhatsAppClient, FakeWhatsAppClient>();
}

// --- Banka entegrasyonu sağlayıcısı: docs/10-decisions.md E1 - WhatsApp'takiyle aynı
// yapısal DI kararı, gerçek sağlayıcı (PayTR/Papara İşletme/vb.) henüz seçilmedi.
//
// Fake   = sahte ama gerçekçi görünen IBAN üretir; YALNIZCA dev/test (ProductionSecretsGuard reddeder).
// Manual = banka entegrasyonu kapalı; sanal IBAN tahsisi açık bir hatayla reddedilir, admin
//          ödemeyi elle girer. Production'da geçerli - gerçek sağlayıcı seçilene kadar okulun
//          canlıya çıkmasını bu karar bloke etmesin diye var.
// Geçerli değerler BankingProviderModes'ta tek yerde tanımlı - ProductionSecretsGuard da
// aynı kaynağı kullanır, aksi halde ikisi ayrışıp uygulamayı hiç başlayamaz hale getirebilir.
var bankingProvider = builder.Configuration["Banking:Provider"] ?? BankingProviderModes.Fake;
if (!BankingProviderModes.IsSupported(bankingProvider))
{
    throw new InvalidOperationException($"Bilinmeyen Banking:Provider değeri: '{bankingProvider}'. Şu an '{BankingProviderModes.Fake}' (dev) ve '{BankingProviderModes.Manual}' (banka entegrasyonu kapalı) destekleniyor; gerçek sağlayıcı seçilince buraya yeni bir IBankPaymentProvider eklenir (bkz. docs/12-bank-integration.md).");
}
if (string.Equals(bankingProvider, BankingProviderModes.Manual, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IBankPaymentProvider, ManualBankPaymentProvider>();
}
else
{
    builder.Services.AddSingleton<IBankPaymentProvider, FakeBankPaymentProvider>();
}

// --- Yedekleme hedefi: docs/10-decisions.md G - kullanıcının kendi sunucusuna SFTP/SSH. ---
builder.Services.Configure<SftpBackupStorageOptions>(builder.Configuration.GetSection("Backup:Sftp"));
var backupProvider = builder.Configuration["Backup:Provider"] ?? "Fake";
if (string.Equals(backupProvider, "Sftp", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IBackupStorage, SftpBackupStorage>();
}
else
{
    builder.Services.AddSingleton<IBackupStorage, FakeBackupStorage>();
}

// --- E-posta alarmı: docs/10-decisions.md G - en kolay kurulum Gmail SMTP + uygulama şifresi,
// ama kod herhangi bir SMTP sağlayıcısıyla çalışır. ---
builder.Services.Configure<SmtpEmailSenderOptions>(builder.Configuration.GetSection("Email:Smtp"));
var emailProvider = builder.Configuration["Email:Provider"] ?? "Fake";
if (string.Equals(emailProvider, "Smtp", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender, FakeEmailSender>();
}

// --- AI (Faz 10, "yapıcı metne dönüştür"): OPSİYONEL özellik. ---
// Yapılandırılmadığında Disabled implementasyonu devreye girer ve gelişim akışı AI olmadan
// eksiksiz çalışmaya devam eder - manuel yorum yazma/onaylama hiç etkilenmez.
// WhatsApp/Banking ile aynı yapısal DI kararı, bu yüzden Build()'den önce okunuyor.
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
var aiProvider = builder.Configuration["Ai:Provider"] ?? "Disabled";
if (string.Equals(aiProvider, "OpenAi", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IConstructiveTextRewriter, OpenAiConstructiveTextRewriter>();
}
else
{
    builder.Services.AddSingleton<IConstructiveTextRewriter, DisabledConstructiveTextRewriter>();
}

// --- Kimlik doğrulama: httpOnly cookie oturumu (docs/10-decisions.md B4 - JWT'nin
// refresh/iptal derdi 8 kullanıcılık sistemde karşılıksız) ---
// Not: builder.Configuration burada bir kapanış (closure) içinde okunuyor - bu delegate
// Build()'den çok sonra (ilk istekte) çalıştığı için test override'ları düzgün yansır.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = builder.Configuration["Auth:CookieName"] ?? "abdera_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(builder.Configuration.GetValue("Auth:SessionHours", 12));
        options.SlidingExpiration = true;
        // API çağrısı olduğu için 302 yönlendirme yerine 401/403 döner.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options => options.AddAbderaAuthorizationPolicies());

// --- Ortak hata modeli (RFC 7807 ProblemDetails) ---
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// --- Sağlık kontrolü (Actuator karşılığı) ---
builder.Services.AddHealthChecks()
    .AddNpgSql(ResolveConnectionString, name: "postgresql");

// --- OpenAPI (Swagger yerine .NET'in yerleşik OpenAPI üretimi) ---
builder.Services.AddOpenApi();

// --- CORS: yalnızca frontend origin'ine izin ver, cookie taşınabilsin diye credentials açık ---
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:3000")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// --- Rate limiting (SEC-3, docs/13-audit-fix-prompt.md): ASP.NET Core'un yerleşik
// Microsoft.AspNetCore.RateLimiting'i kullanıyoruz - zaten paylaşılan çerçevede (shared
// framework) geliyor, ek NuGet paketi gerekmiyor (CLAUDE.md "gereksiz bağımlılık ekleme").
// Politika delegate'leri her istekte çalışır (Build()'den sonra) - RateLimiting:* ayarları
// burada değil, IConfiguration üzerinden istek anında okunur, bu yüzden
// WebApplicationFactory'nin test-time override'ı sorunsuz yansır.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Reddedilen istek boş gövdeyle dönerse istemci "Bir hata oluştu" demekten başka bir şey
    // yapamıyordu (frontend'in ApiError'ı ProblemDetails bekliyor) - kullanıcı neden
    // giremediğini ve ne kadar bekleyeceğini bilmiyordu. Aynı sözleşmeyle (RFC 7807, bkz.
    // Shared/GlobalExceptionHandler.cs) Türkçe bir gövde ve mümkünse Retry-After yazıyoruz.
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(
            System.Threading.RateLimiting.MetadataName.RetryAfter, out var retry)
            ? (TimeSpan?)retry
            : null;
        if (retryAfter is { } wait)
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
        }

        var minutes = retryAfter is { } window ? (int)Math.Ceiling(window.TotalMinutes) : 0;
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.29",
            title = "Çok fazla deneme",
            status = StatusCodes.Status429TooManyRequests,
            detail = minutes > 0
                ? $"Güvenlik için bu işlem geçici olarak kısıtlandı. Yaklaşık {minutes} dakika sonra tekrar dene."
                : "Güvenlik için bu işlem geçici olarak kısıtlandı. Kısa bir süre sonra tekrar dene.",
        }, cancellationToken);
    };

    // /api/auth/login: IP başına sabit pencere (öneri: 5 istek / 15 dakika) - kaba kuvvet
    // saldırısına karşı. Testlerde CreateAdminClientAsync onlarca kez çağrıldığından
    // (bkz. MessagingFlowTests vb.), test factory bu limiti config override'ıyla
    // pratikte devre dışı bırakacak kadar yükseltiyor; SEC-3 testi kendi düşük limitli
    // factory'sini WithWebHostBuilder ile kurar.
    options.AddPolicy("auth-login", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIp(httpContext),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimiting:LoginPermitLimit", 5),
                Window = TimeSpan.FromMinutes(config.GetValue("RateLimiting:LoginWindowMinutes", 15)),
                QueueLimit = 0,
            });
    });

    // /api/guardian/otp/*: auth-login ile aynı desen - kaba kuvvetle kod tahmini veya bir
    // veliyi OTP mesajlarıyla bombalamayı (WhatsApp maliyeti + rahatsızlık) önler.
    options.AddPolicy("guardian-otp", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIp(httpContext),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimiting:GuardianOtpPermitLimit", 5),
                Window = TimeSpan.FromMinutes(config.GetValue("RateLimiting:GuardianOtpWindowMinutes", 15)),
                QueueLimit = 0,
            });
    });

    // Webhook uçları (WhatsApp/banka sağlayıcısı) tamamen sınırsız kalmasın ama gerçek
    // sağlayıcı trafiğini de engellemesin - IP başına dakikada 60 istek.
    options.AddPolicy("webhooks", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIp(httpContext),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimiting:WebhookPermitLimitPerMinute", 60),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
});

static string GetClientIp(HttpContext httpContext) =>
    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

var app = builder.Build();

ProductionSecretsGuard.EnsureConfigured(app);

// --- Reverse proxy (Caddy) arkasında çalışırken istemcinin gerçek IP'si ve şeması ---
// Bunlar olmadan: (1) rate limiting partition'ı Connection.RemoteIpAddress'e bakar, bu da
// proxy'nin IP'sidir - tüm okul TEK kovaya düşer ve bir kişinin 5 hatalı girişi herkesi
// kilitler (bkz. GetClientIp); (2) uygulama isteği "http" sanar, HTTPS'e bağlı davranışlar
// yanlış çalışır. UseForwardedHeaders diğer TÜM middleware'lerden önce gelmeli ki
// Serilog/rate limiter/auth düzeltilmiş değerleri görsün.
//
// KnownIPNetworks/KnownProxies temizleniyor: Docker'ın köprü ağı sabit bir adres aralığı
// vermez, varsayılan listede olmayan bir kaynaktan gelen header'lar SESSİZCE yok sayılır.
// Bu, yalnızca proxy'nin erişebildiği bir iç ağda güvenli - api portu dışarı publish
// EDİLMEZ (docker-compose.yml), yani header'ları yalnızca Caddy set edebilir.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

app.UseSerilogRequestLogging();
// Request logging exception handler'in disinda kalir; boylece kontrollu domain
// istisnalari handler tarafindan RFC 7807/4xx'e cevrildikten sonra Serilog gercek son
// status'u gorur. Tersi sira istemci 400 alirken logda sahte 500 + stack trace uretiyordu.
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() }),
        });
    },
});

app.MapAuthModule();
app.MapDashboardModule();
app.MapPeopleModule();
app.MapSchedulingModule();
app.MapAttendanceModule();
app.MapProgressModule();
app.MapPricingModule();
app.MapBillingModule();
app.MapMessagingModule();
app.MapBankingModule();
app.MapOpsModule();

if (app.Environment.IsDevelopment())
{
    app.MapDevelopmentMockData();
}

await DatabaseMigrator.RunAsync(app);
await AdminBootstrapper.RunAsync(app);

app.Run();

// Testlerin WebApplicationFactory<Program> ile bu projeyi başlatabilmesi için görünür yap.
public partial class Program;
