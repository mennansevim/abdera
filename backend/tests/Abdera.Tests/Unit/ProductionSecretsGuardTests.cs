using Abdera.Api.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Abdera.Tests.Unit;

// SEC-1/SEC-2: Production'da WhatsApp:AppSecret/PayloadSigningKey eksikse uygulama
// başlamayı reddetmeli (bkz. Program.cs, docs/13-audit-fix-prompt.md madde 1.3/2).
public class ProductionSecretsGuardTests
{
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
        var app = BuildApp("Production", new Dictionary<string, string?>
        {
            ["WhatsApp:AppSecret"] = "real-secret",
            ["WhatsApp:PayloadSigningKey"] = "real-signing-key",
        });

        ProductionSecretsGuard.EnsureConfigured(app);
    }

    [Fact]
    public void Does_not_throw_in_development_even_when_secrets_are_missing()
    {
        var app = BuildApp("Development", new Dictionary<string, string?>());

        ProductionSecretsGuard.EnsureConfigured(app);
    }
}
