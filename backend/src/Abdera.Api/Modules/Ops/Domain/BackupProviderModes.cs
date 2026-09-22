namespace Abdera.Api.Modules.Ops.Domain;

public static class BackupProviderModes
{
    public const string Fake = "Fake";
    public const string Sftp = "Sftp";
    public const string Disabled = "Disabled";

    public static bool IsSupported(string? value) =>
        string.Equals(value, Fake, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Sftp, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Disabled, StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowedInProduction(string? value) =>
        string.Equals(value, Sftp, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Disabled, StringComparison.OrdinalIgnoreCase);
}
