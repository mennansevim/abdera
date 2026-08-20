using Abdera.Api.Modules.Attendance;
using Abdera.Api.Modules.Auth;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Infrastructure;
using Abdera.Api.Modules.People;
using Abdera.Api.Modules.Pricing;
using Abdera.Api.Modules.Progress;
using Abdera.Api.Modules.Scheduling;
using Abdera.Api.Shared;
using HealthChecks.NpgSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
builder.Services.AddBillingModule();

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

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseCors();
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
app.MapPeopleModule();
app.MapSchedulingModule();
app.MapAttendanceModule();
app.MapProgressModule();
app.MapPricingModule();
app.MapBillingModule();

await DatabaseMigrator.RunAsync(app);
await AdminBootstrapper.RunAsync(app);

app.Run();

// Testlerin WebApplicationFactory<Program> ile bu projeyi başlatabilmesi için görünür yap.
public partial class Program;
