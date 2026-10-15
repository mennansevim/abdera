using System.Text;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Provisioning;
using Microsoft.Extensions.Configuration;

namespace Abdera.Platform.Tests;

public class PlatformUnitTests
{
    [Fact]
    public void Totp_matches_rfc6238_sha1_vector()
    {
        // RFC 6238 Ek B: anahtar "12345678901234567890", T=59 → 94287082 (8 hane); 6 hane = 287082.
        var secret = Base32("12345678901234567890"u8.ToArray());
        Assert.Equal("287082", Totp.CodeAt(secret, 59 / 30));
        var now = DateTimeOffset.FromUnixTimeSeconds(59);
        Assert.Equal(1, Totp.Verify(secret, "287082", now, lastAcceptedStep: 0));
        Assert.Null(Totp.Verify(secret, "287082", now, lastAcceptedStep: 1));
        Assert.Null(Totp.Verify(secret, "12a456", now, lastAcceptedStep: 0));
    }

    private static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { output.Append(alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) output.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    [Theory]
    [InlineData("deniz; rm -rf /")]
    [InlineData("deniz$(id)")]
    [InlineData("Deniz")]
    [InlineData("ab")]
    public void Agent_command_rejects_unsafe_slugs(string slug) =>
        Assert.Throws<ArgumentException>(() => AgentCommand.For("status", slug));

    [Fact]
    public void Agent_command_rejects_unsafe_arguments_and_unknown_verbs()
    {
        Assert.Throws<ArgumentException>(() => AgentCommand.For("route add", "deniz", ["deniz.abdera.app;reboot"]));
        Assert.Throws<ArgumentException>(() => AgentCommand.For("rm", "deniz"));
        Assert.Equal("route add deniz deniz.abdera.app --op abcdef12-3-1",
            AgentCommand.For("route add", "deniz", ["deniz.abdera.app"], "abcdef12-3-1").Text);
    }

    private static readonly Dictionary<string, string> BaseSecrets = new()
    {
        [SecretNames.PostgresPassword] = "pg", [SecretNames.BackupEncryptionKey] = "key", [SecretNames.BackupGatewayPassword] = "gw",
        [SecretNames.ProvisioningToken] = "prov", [SecretNames.TelemetryToken] = "tel", [SecretNames.AlertsSecret] = "alr",
        [SecretNames.WhatsAppPayloadSigningKey] = "sign",
    };

    private static Release Release() => Api.Domain.Release.Create("2026.10.15.1",
        PlatformFactory.Image("abdera-api", 'a'), PlatformFactory.Image("abdera-web", 'a'), null, "[]", null, DateTimeOffset.UtcNow);

    [Fact]
    public void Env_enables_a_provider_only_when_its_module_and_all_secrets_are_present()
    {
        var builder = new SchoolEnvBuilder(new ConfigurationBuilder().Build());
        var school = School.Create("deniz", "Deniz", "Deniz", "deniz.abdera.app", "2026.10.15.1", ["whatsapp"], null, DateTimeOffset.UtcNow);
        var secrets = new Dictionary<string, string>(BaseSecrets)
        {
            [SecretNames.IntegrationPrefix + "WhatsApp__AccessToken"] = "t",
            [SecretNames.IntegrationPrefix + "WhatsApp__AppSecret"] = "s",
            [SecretNames.IntegrationPrefix + "WhatsApp__PhoneNumberId"] = "p",
        };
        Assert.Contains("WhatsApp__Provider=Disabled\n", builder.Build(school, Release(), secrets));

        secrets[SecretNames.IntegrationPrefix + "WhatsApp__WebhookVerifyToken"] = "v";
        var env = builder.Build(school, Release(), secrets);
        Assert.Contains("WhatsApp__Provider=Cloud\n", env);
        Assert.Contains("WhatsApp__AccessToken=t\n", env);
        Assert.Contains("Email__Provider=Disabled\n", env);

        secrets[SecretNames.IntegrationPrefix + "WhatsApp__AccessToken"] = "a\nModules__Enabled=";
        Assert.Throws<InvalidOperationException>(() => builder.Build(school, Release(), secrets));
    }

    [Fact]
    public void Alerts_webhook_is_written_only_for_an_https_platform()
    {
        var school = School.Create("deniz", "Deniz", "Deniz", "deniz.abdera.app", "2026.10.15.1", [], null, DateTimeOffset.UtcNow);
        string Env(string url) => new SchoolEnvBuilder(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:PublicUrl"] = url }).Build())
            .Build(school, Release(), BaseSecrets);

        Assert.Contains("Alerts__WebhookUrl=https://platform.abdera.app/api/alerts\n", Env("https://platform.abdera.app"));
        // Okul uygulaması http alarm adresiyle açılmaz; yerel test ortamında alarm kapalı kalır.
        Assert.Contains("Alerts__WebhookUrl=\n", Env("http://localhost:8090"));
    }

    [Fact]
    public void Empty_module_selection_is_written_as_none()
    {
        var school = School.Create("bos", "Boş", "Boş", "bos.abdera.app", "2026.10.15.1", [], null, DateTimeOffset.UtcNow);
        Assert.Equal("none", school.ModulesEnvValue);
        Assert.Contains("Modules__Enabled=none\n", new SchoolEnvBuilder(new ConfigurationBuilder().Build()).Build(school, Release(), BaseSecrets));
    }

    [Theory]
    [InlineData("2026.10.20.1", "2026.10.15.9", 1)]
    [InlineData("2026.10.15.10", "2026.10.15.9", 1)]
    [InlineData("2026.10.15.1", "2026.10.15.1", 0)]
    public void Release_versions_compare_numerically(string left, string right, int sign) =>
        Assert.Equal(sign, Math.Sign(Api.Domain.Release.Compare(left, right)));
}
