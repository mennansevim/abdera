using Abdera.Api.Modules.Banking.Domain;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Abdera.Tests.Unit;

// SEC-1/SEC-2: Production'da WhatsApp:AppSecret/PayloadSigningKey eksikse uygulama
// başlamayı reddetmeli (bkz. Program.cs, docs/13-audit-fix-prompt.md madde 1.3/2).
public class ProductionSecretsGuardTests
{
    private static Dictionary<string, string?> CompleteProductionConfiguration() => new()
    {
        ["WhatsApp:Provider"] = "Cloud",
        ["WhatsApp:AppSecret"] = "real-secret-value",
        ["WhatsApp:PayloadSigningKey"] = "real-signing-key",
        ["WhatsApp:PhoneNumberId"] = "phone-id",
        ["WhatsApp:AccessToken"] = "access-token-value",
        ["WhatsApp:WebhookVerifyToken"] = "verify-token",
        ["Backup:Provider"] = "Sftp",
        ["Backup:EncryptionKey"] = "base64-encryption-key-value",
        ["Backup:Sftp:Host"] = "backup.internal",
        ["Backup:Sftp:PrivateKeyPath"] = "/run/secrets/backup_key",
        // Program.cs'in gerçekten DI'a kaydedebildiği bir değer olmalı - aksi halde bu test
        // "geçerli" saydığı bir konfigürasyonla uygulamanın Production'da hiç ayağa
        // kalkamayacağını gizler (bkz. BankingProviderModesTests). Manual = banka
        // entegrasyonu kapalı; webhook kullanılmadığı için paylaşılan sır da beklenmez.
        ["Banking:Provider"] = BankingProviderModes.Manual,
        ["Bootstrap:AdminPassword"] = "strong-admin-secret",
    };

    private static WebApplication BuildApp(string environmentName, Dictionary<string, string?> config)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.Configuration.AddInMemoryCollection(config);
        return builder.Build();
    }

    [Fact]
    public void Throws_in_production_when_both_secrets_are_missing()
    {
        var app = BuildApp("Production", new Dictionary<string, string?>());

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsGuard.EnsureConfigured(app));
        Assert.Contains("WhatsApp__AppSecret", ex.Message);
        Assert.Contains("WhatsApp__PayloadSigningKey", ex.Message);
    }

    [Fact]
    public void Throws_in_production_when_only_one_secret_is_missing()
    {
        var app = BuildApp("Production", new Dictionary<string, string?>
        {
            ["WhatsApp:AppSecret"] = "real-secret",
        });

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsGuard.EnsureConfigured(app));
        Assert.DoesNotContain("WhatsApp__AppSecret", ex.Message);
        Assert.Contains("WhatsApp__PayloadSigningKey", ex.Message);
    }

    [Fact]
    public void Does_not_throw_in_production_when_both_secrets_are_configured()
    {
        var app = BuildApp("Production", CompleteProductionConfiguration());

        ProductionSecretsGuard.EnsureConfigured(app);
    }

    [Fact]
    public void Does_not_throw_in_development_even_when_secrets_are_missing()
    {
        var app = BuildApp("Development", new Dictionary<string, string?>());

        ProductionSecretsGuard.EnsureConfigured(app);
    }

    [Fact]
    public void Throws_in_production_when_cloud_provider_sending_configuration_is_missing()
    {
        var app = BuildApp("Production", new Dictionary<string, string?>
        {
            ["WhatsApp:Provider"] = "Cloud",
            ["WhatsApp:AppSecret"] = "real-secret",
            ["WhatsApp:PayloadSigningKey"] = "real-signing-key",
        });

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsGuard.EnsureConfigured(app));

        Assert.Contains("WhatsApp__PhoneNumberId", ex.Message);
        Assert.Contains("WhatsApp__AccessToken", ex.Message);
        Assert.Contains("WhatsApp__WebhookVerifyToken", ex.Message);
    }

    [Fact]
    public void Does_not_throw_in_production_when_cloud_provider_configuration_is_complete()
    {
        var app = BuildApp("Production", CompleteProductionConfiguration());

        ProductionSecretsGuard.EnsureConfigured(app);
    }

    [Theory]
    [InlineData("WhatsApp:Provider", "Fake", "WhatsApp__Provider=Cloud")]
    [InlineData("Backup:Provider", "Fake", "Backup__Provider=Sftp")]
    [InlineData("Banking:Provider", "Fake", "Banking__Provider")]
    public void Throws_in_production_when_a_fake_provider_is_active(string key, string value, string expected)
    {
        var configuration = CompleteProductionConfiguration();
        configuration[key] = value;
        var app = BuildApp("Production", configuration);

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsGuard.EnsureConfigured(app));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Throws_in_production_when_a_placeholder_secret_is_used()
    {
        var configuration = CompleteProductionConfiguration();
        configuration["Bootstrap:AdminPassword"] = "<ILK-GIRISTE-DEGISTIR>";
        var app = BuildApp("Production", configuration);

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsGuard.EnsureConfigured(app));

        Assert.Contains("Bootstrap__AdminPassword (placeholder olamaz)", ex.Message);
    }
}
