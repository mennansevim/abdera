using System.Globalization;
using System.Text.Json;

namespace Abdera.Platform.Api.Domain;

// Okul sunucusunun ölçümü (gözlem paneli): ajanın `host-stats` çıktısından. Yalnızca sayılar ve konteyner
// adları; okul verisi yok. Konteyner satırları jsonb olarak tutulur (sayısı sunucudaki okullara göre değişir).
public class HostSnapshot
{
    public Guid Id { get; private set; }
    public Guid HostId { get; private set; }
    public DateTimeOffset TakenAt { get; private set; }
    public bool Reachable { get; private set; }
    public decimal? Load1 { get; private set; }
    public decimal? Load5 { get; private set; }
    public int? Cpus { get; private set; }
    public int? MemTotalMb { get; private set; }
    public int? MemAvailableMb { get; private set; }
    public int? DiskTotalMb { get; private set; }
    public int? DiskFreeMb { get; private set; }
    public long? UptimeSeconds { get; private set; }
    public string ContainersJson { get; private set; } = "[]";
    public string? Error { get; private set; }

    public IReadOnlyList<ContainerStat> Containers =>
        JsonSerializer.Deserialize<List<ContainerStat>>(ContainersJson, JsonOptions) ?? [];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HostSnapshot() { }

    public static HostSnapshot Unreachable(Guid hostId, string error, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), HostId = hostId, TakenAt = now, Reachable = false,
        Error = error.Length > 500 ? error[..500] : error,
    };

    // Ajan çıktısı: {"load1":..,"memTotalMb":..,"stats":[docker stats satırları],"containers":[inspect satırları]}.
    // docker stats belleği "150MiB / 384MiB", CPU'yu "1.5%" diye yazar; burada sayıya çevrilir.
    public static HostSnapshot Parse(Guid hostId, string agentOutput, DateTimeOffset now)
    {
        var root = JsonDocument.Parse(agentOutput).RootElement;
        var stats = root.TryGetProperty("stats", out var s) && s.ValueKind == JsonValueKind.Array
            ? s.EnumerateArray().ToDictionary(
                item => item.GetProperty("Name").GetString()!.TrimStart('/'),
                item => (Mem: ParseMemUsage(item.TryGetProperty("MemUsage", out var m) ? m.GetString() : null),
                         Cpu: ParsePercent(item.TryGetProperty("CPUPerc", out var c) ? c.GetString() : null)))
            : [];
        var containers = new List<ContainerStat>();
        if (root.TryGetProperty("containers", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString()!.TrimStart('/');
                var stat = stats.GetValueOrDefault(name);
                containers.Add(new ContainerStat(
                    name, SchoolSlugOf(name), item.GetProperty("status").GetString() ?? "?",
                    item.TryGetProperty("health", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null,
                    item.TryGetProperty("restarts", out var r) ? r.GetInt32() : 0,
                    stat.Mem.Used, stat.Mem.Limit, stat.Cpu));
            }
        }
        return new HostSnapshot
        {
            Id = Guid.NewGuid(), HostId = hostId, TakenAt = now, Reachable = true,
            Load1 = Decimal(root, "load1"), Load5 = Decimal(root, "load5"), Cpus = Int(root, "cpus"),
            MemTotalMb = Int(root, "memTotalMb"), MemAvailableMb = Int(root, "memAvailableMb"),
            DiskTotalMb = Int(root, "diskTotalMb"), DiskFreeMb = Int(root, "diskFreeMb"),
            UptimeSeconds = root.TryGetProperty("uptimeSeconds", out var u) && u.TryGetInt64(out var up) ? up : null,
            ContainersJson = JsonSerializer.Serialize(containers.OrderBy(c => c.Name, StringComparer.Ordinal), JsonOptions),
        };
    }

    // abdera-<okul>-<servis>-<n> (compose projesi abdera-<okul>); altyapı konteynerleri (abdera-caddy,
    // abdera-pi-*) bir okula ait değildir.
    public static string? SchoolSlugOf(string container)
    {
        var parts = container.Split('-');
        if (parts.Length < 4 || parts[0] != "abdera" || !int.TryParse(parts[^1], out _)) return null;
        var service = parts[^2] == "gw" && parts.Length >= 5 ? $"{parts[^3]}-gw" : parts[^2];
        var slugParts = parts.Length - 2 - (service.Contains('-') ? 2 : 1);
        return slugParts < 1 ? null : string.Join('-', parts[1..(1 + slugParts)]);
    }

    public static (decimal? Used, decimal? Limit) ParseMemUsage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var halves = value.Split('/', StringSplitOptions.TrimEntries);
        return (ParseSize(halves[0]), halves.Length > 1 ? ParseSize(halves[1]) : null);
    }

    // MiB cinsinden; docker "B, KiB, MiB, GiB" (ikilik) ve bazen "kB, MB, GB" (onluk) yazar.
    public static decimal? ParseSize(string value)
    {
        var unitStart = 0;
        while (unitStart < value.Length && (char.IsDigit(value[unitStart]) || value[unitStart] == '.')) unitStart++;
        if (!decimal.TryParse(value[..unitStart], NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) return null;
        var factor = value[unitStart..].Trim() switch
        {
            "B" => 1m / (1024 * 1024),
            "KiB" => 1m / 1024, "kB" or "KB" => 1000m / (1024 * 1024),
            "MiB" => 1m, "MB" => 1_000_000m / (1024 * 1024),
            "GiB" => 1024m, "GB" => 1_000_000_000m / (1024 * 1024),
            "TiB" => 1024m * 1024,
            _ => (decimal?)null,
        };
        return factor is { } f ? decimal.Round(number * f, 1) : null;
    }

    public static decimal? ParsePercent(string? value) =>
        decimal.TryParse(value?.TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static decimal? Decimal(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDecimal(out var number) ? number : null;

    private static int? Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
}

public record ContainerStat(string Name, string? School, string Status, string? Health, int Restarts, decimal? MemMb, decimal? MemLimitMb, decimal? CpuPercent);
