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
        ["Backup:EncryptionKey"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        ["Backup:Sftp:Host"] = "backup.internal",
        ["Backup:Sftp:Username"] = "backup-user",
        ["Backup:Sftp:PrivateKeyPath"] = "/run/secrets/backup_key",
        // Program.cs'in gerçekten DI'a kaydedebildiği bir değer olmalı - aksi halde bu test
        // "geçerli" saydığı bir konfigürasyonla uygulamanın Production'da hiç ayağa
        // kalkamayacağını gizler (bkz. BankingProviderModesTests). Manual = banka
        // entegrasyonu kapalı; webhook kullanılmadığı için paylaşılan sır da beklenmez.
        ["Banking:Provider"] = BankingProviderModes.Manual,
        ["Bootstrap:AdminPassword"] = "strong-admin-secret",
        ["Frontend:Origin"] = "https://abdera.example",
        ["Auth:PersistKeysToDatabase"] = "true",
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
        var configuration = CompleteProductionConfiguration();
        configuration.Remove("WhatsApp:AppSecret");
        configuration.Remove("WhatsApp:PayloadSigningKey");
        var app = BuildApp("Production", configuration);

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsGuard.EnsureConfigured(app));
        Assert.Contains("WhatsApp__AppSecret", ex.Message);
        Assert.Contains("WhatsApp__PayloadSigningKey", ex.Message);
    }

    [Fact]
    public void Throws_in_production_when_only_one_secret_is_missing()
    {
        var configuration = CompleteProductionConfiguration();
        configuration.Remove("WhatsApp:PayloadSigningKey");
        var app = BuildApp("Production", configuration);

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

    [Fact]
    public void Does_not_require_integration_secrets_when_optional_providers_are_disabled()
    {
        var configuration = CompleteProductionConfiguration();
        configuration["WhatsApp:Provider"] = "Disabled";
        configuration["Backup:Provider"] = "Disabled";
        foreach (var key in configuration.Keys
                     .Where(key => key.StartsWith("WhatsApp:", StringComparison.Ordinal) ||
                                   key.StartsWith("Backup:", StringComparison.Ordinal))
                     .Where(key => !key.EndsWith(":Provider", StringComparison.Ordinal))
                     .ToList())
        {
            configuration.Remove(key);
        }
        var app = BuildApp("Production", configuration);

        ProductionSecretsGuard.EnsureConfigured(app);
    }

    [Theory]
    [InlineData("WhatsApp:Provider", "Fake", "WhatsApp__Provider")]
    [InlineData("Backup:Provider", "Fake", "Backup__Provider")]
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

    [Theory]
    [InlineData(null)]
    [InlineData("http://abdera.example")]
    [InlineData("https://localhost")]
    public void Throws_in_production_when_frontend_origin_is_not_a_public_https_origin(string? origin)
    {
        var configuration = CompleteProductionConfiguration();
        configuration["Frontend:Origin"] = origin;
        var app = BuildApp("Production", configuration);

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsGuard.EnsureConfigured(app));

        Assert.Contains("Frontend__Origin", ex.Message);
    }

    [Fact]
    public void Throws_in_production_when_demo_mode_is_enabled()
    {
        var configuration = CompleteProductionConfiguration();
        configuration["Demo:Enabled"] = "true";
        var app = BuildApp("Production", configuration);

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsGuard.EnsureConfigured(app));

        Assert.Contains("Demo__Enabled=false", ex.Message);
    }

    [Fact]
    public void Throws_in_production_when_passwordless_dev_login_is_enabled()
    {
        var configuration = CompleteProductionConfiguration();
        configuration["Auth:DevLogin:Enabled"] = "true";
        var app = BuildApp("Production", configuration);

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsGuard.EnsureConfigured(app));

        Assert.Contains("Auth__DevLogin__Enabled=false", ex.Message);
    }

    [Fact]
    public void Throws_in_production_when_data_protection_keys_are_ephemeral()
    {
        var configuration = CompleteProductionConfiguration();
        configuration["Auth:PersistKeysToDatabase"] = "false";
        configuration["Auth:KeysDirectory"] = null;
        var app = BuildApp("Production", configuration);

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsGuard.EnsureConfigured(app));

        Assert.Contains("Auth__PersistKeysToDatabase", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("c2hvcnQ=")]
    public void Throws_in_production_when_backup_encryption_key_is_not_a_32_byte_base64_value(string key)
    {
        var configuration = CompleteProductionConfiguration();
        configuration["Backup:EncryptionKey"] = key;
        var app = BuildApp("Production", configuration);

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsGuard.EnsureConfigured(app));

        Assert.Contains("Backup__EncryptionKey", ex.Message);
    }
}
