namespace Abdera.Api.Modules.Messaging.Domain;

public static class WhatsAppProviderModes
{
    public const string Fake = "Fake";
    public const string Cloud = "Cloud";
    public const string Disabled = "Disabled";

    public static bool IsSupported(string? value) =>
        string.Equals(value, Fake, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Cloud, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Disabled, StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowedInProduction(string? value) =>
        string.Equals(value, Cloud, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Disabled, StringComparison.OrdinalIgnoreCase);
}
