using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Features;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Persistence;
using Abdera.Platform.Api.Provisioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Abdera Platform API (docs/multi-tenant/ARCHITECTURE.md §3). Okulları kurar, günceller, izler.
// Okul uygulamasından ayrı bir süreç ve ayrı bir veritabanı; okulların kişisel verisine erişmez.
var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Bağlantı dizesi DI çözümlenirken okunur (okul uygulamasındaki kural: test overlay'i görülsün).
builder.Services.AddDbContext<PlatformDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Platform")
        ?? throw new InvalidOperationException("ConnectionStrings:Platform tanımlı değil.")));

builder.Services.AddSingleton(TimeProvider.System);

// Sır anahtarları: kaybolursa tüm okulların sırları okunamaz - dizin ayrıca yedeklenmeli (§11).
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Abdera.Platform");
if (builder.Configuration["Platform:KeysDirectory"] is { Length: > 0 } keysDirectory)
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

builder.Services.AddScoped<SecretVault>();
builder.Services.AddSingleton<IDataProtectionVault, OperatorSecretProtector>();
builder.Services.AddSingleton<IPasswordHasher<PlatformUser>, PasswordHasher<PlatformUser>>();
builder.Services.AddSingleton<SchoolEnvBuilder>();
builder.Services.AddScoped<ProvisioningEngine>();

// Sağlayıcılar yapısal DI kararıdır (okul uygulamasındaki WhatsApp/Banking deseni): Fake = sunucu,
// DNS ve okul olmadan tüm akışın çalıştığı geliştirme/test modu. Production'da Fake reddedilir.
var agentProvider = builder.Configuration["HostAgent:Provider"] ?? "Fake";
var dnsProvider = builder.Configuration["Dns:Provider"] ?? "Fake";
var schoolClientProvider = builder.Configuration["SchoolClient:Provider"] ?? "Fake";
if (agentProvider == "Ssh") builder.Services.AddSingleton<IHostAgent, SshHostAgent>();
else builder.Services.AddSingleton<IHostAgent, FakeHostAgent>();
if (dnsProvider == "Cloudflare") builder.Services.AddHttpClient<IDnsProvider, CloudflareDnsProvider>();
else builder.Services.AddSingleton<IDnsProvider, FakeDnsProvider>();
if (schoolClientProvider == "Http") builder.Services.AddHttpClient<ISchoolClient, HttpSchoolClient>(client => client.Timeout = TimeSpan.FromSeconds(15));
else builder.Services.AddSingleton<ISchoolClient, FakeSchoolClient>();

builder.Services.AddSingleton<ProvisioningWorker>();
builder.Services.AddSingleton<RolloutWorker>();
builder.Services.AddSingleton<TelemetryPoller>();
if (builder.Configuration.GetValue("Platform:RunWorkers", true))
{
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ProvisioningWorker>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<RolloutWorker>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<TelemetryPoller>());
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "abdera_platform";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    // Parola/TOTP değişince eski oturumlar düşer.
    options.Events.OnValidatePrincipal = async context =>
    {
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal?.FindFirstValue(Auth.StampClaim);
        var db = context.HttpContext.RequestServices.GetRequiredService<PlatformDbContext>();
        var user = Guid.TryParse(id, out var userId) ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId) : null;
        if (user is null || user.SecurityStamp.ToString() != stamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    };
});
builder.Services.AddAuthorization(options => options.AddPolicy(Auth.OperatorPolicy, policy =>
    policy.RequireAuthenticatedUser().RequireClaim(Auth.TotpClaim, "true")));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPermitLimit", 10), Window = TimeSpan.FromMinutes(15) }));
    options.AddPolicy("alerts", http => RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddProblemDetails();
// Üretimde panel ve API aynı origin'de (Caddy). Geliştirmede panel ayrı portta: yalnızca yapılandırılmış
// origin'e, çerezli istek için izin verilir. Origin, istek anında DI'dan okunur (test overlay'i görülsün).
builder.Services.AddCors();

var app = builder.Build();

if (app.Environment.IsProduction())
{
    // Production'da sahte sağlayıcı ya da kalıcı olmayan anahtar ile açılmak sessiz veri kaybıdır.
    var problems = new List<string>();
    if (agentProvider != "Ssh") problems.Add("HostAgent__Provider=Ssh");
    if (dnsProvider != "Cloudflare") problems.Add("Dns__Provider=Cloudflare");
    if (schoolClientProvider != "Http") problems.Add("SchoolClient__Provider=Http");
    if (string.IsNullOrWhiteSpace(app.Configuration["Platform:KeysDirectory"])) problems.Add("Platform__KeysDirectory");
    if (string.IsNullOrWhiteSpace(app.Configuration["Platform:PublicUrl"])) problems.Add("Platform__PublicUrl");
    if (problems.Count > 0) throw new InvalidOperationException($"Platform production yapılandırması eksik: {string.Join(", ", problems)}");
}

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title, detail, errors) = error switch
    {
        ValidationFailedException v => (400, "Doğrulama hatası", "Doğrulama hatası.", v.Errors),
        NotFoundException n => (404, "Bulunamadı", n.Message, null),
        ConflictException c => (409, "Çakışma", c.Message, null),
        DbUpdateConcurrencyException => (409, "Çakışma", "Kayıt başka biri tarafından değiştirildi; yenileyip tekrar deneyin.", null),
        BadHttpRequestException b => (400, "Geçersiz istek", b.Message, null),
        _ => (500, "Sunucu hatası", "Beklenmeyen bir hata oldu.", (IReadOnlyDictionary<string, string[]>?)null),
    };
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { title, status, detail, errors });
}));

app.UseCors(cors =>
{
    var origins = (app.Configuration["Frontend:Origin"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (origins.Length > 0) cors.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (PlatformDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "Healthy" }) : Results.StatusCode(503));
app.MapAuth();
app.MapHosts();
app.MapReleases();
app.MapSchools();
app.MapRollouts();
app.MapAlerts();
app.MapAudit();

if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.MigrateAsync();
}
await Auth.BootstrapAsync(app);

app.Run();

public partial class Program;
