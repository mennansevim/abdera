using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Infrastructure;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Infrastructure;

namespace Abdera.Tests.Unit;

public class OptionalProviderModesTests
{
    [Theory]
    [InlineData("Disabled")]
    [InlineData("disabled")]
    public void Disabled_modes_are_supported_and_allowed_in_production(string value)
    {
        Assert.True(WhatsAppProviderModes.IsSupported(value));
        Assert.True(WhatsAppProviderModes.IsAllowedInProduction(value));
        Assert.True(BackupProviderModes.IsSupported(value));
        Assert.True(BackupProviderModes.IsAllowedInProduction(value));
    }

    [Theory]
    [InlineData("Fake")]
    [InlineData("")]
    [InlineData(null)]
    public void Fake_and_empty_modes_are_rejected_in_production(string? value)
    {
        Assert.False(WhatsAppProviderModes.IsAllowedInProduction(value));
        Assert.False(BackupProviderModes.IsAllowedInProduction(value));
    }

    [Fact]
    public async Task Disabled_whatsapp_client_fails_closed()
    {
        var client = new DisabledWhatsAppClient();

        var result = await client.SendFreeTextAsync("+905550000000", "test");

        Assert.False(result.Success);
        Assert.Null(result.ProviderMessageId);
        Assert.Contains("devre dışı", result.Error);
    }

    [Fact]
    public async Task Disabled_backup_storage_fails_closed()
    {
        var storage = new DisabledBackupStorage();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.ListAsync());

        Assert.Contains("devre dışı", exception.Message);
    }
}
