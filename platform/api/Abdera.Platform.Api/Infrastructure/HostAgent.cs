using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Abdera.Platform.Api.Domain;
using Renci.SshNet;

namespace Abdera.Platform.Api.Infrastructure;

public record AgentResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
    public string Describe() => Ok ? Output : $"çıkış {ExitCode}: {(string.IsNullOrWhiteSpace(Error) ? Output : Error)}".Trim();
}

// Sunucu ajanı (deploy/host/abdera-host) çağrısı. Komut metni yalnızca AgentCommand ile kurulur:
// her argüman ajanın kendi beyaz listesiyle aynı kurala uyar, kabuk yorumu yoktur (§6.1).
public interface IHostAgent
{
    Task<AgentResult> RunAsync(ServerHost host, AgentCommand command, CancellationToken cancellationToken);
}

public sealed partial record AgentCommand(string Text, string? Stdin)
{
    // verb "route add" gibi iki kelime olabilir; yalnızca sabit metinden gelir, kullanıcı girdisinden değil.
    public static AgentCommand For(string verb, string? slug = null, IEnumerable<string>? args = null, string? operationId = null, string? stdin = null)
    {
        if (!Verbs.Contains(verb)) throw new ArgumentException($"Bilinmeyen ajan komutu: {verb}");
        var parts = new List<string> { verb };
        if (slug is not null)
        {
            if (!Slug.IsValid(slug) && !(slug.EndsWith("-drill") && Slug.IsValid(slug[..^6])))
                throw new ArgumentException($"Geçersiz kısaltma: {slug}");
            parts.Add(slug);
        }
        foreach (var arg in args ?? [])
        {
            if (!SafeArg().IsMatch(arg)) throw new ArgumentException($"Ajan argümanı güvenli değil: {arg}");
            parts.Add(arg);
        }
        if (operationId is not null)
        {
            if (!OperationIdPattern().IsMatch(operationId)) throw new ArgumentException("Geçersiz işlem kimliği.");
            parts.Add("--op");
            parts.Add(operationId);
        }
        return new AgentCommand(string.Join(' ', parts), stdin);
    }

    private static readonly HashSet<string> Verbs =
    [
        "create", "reconfigure", "pull", "up", "down", "upgrade", "rollback", "route add", "route remove", "route alias", "route alias-remove", "suspend", "resume",
        "provision", "backup-now", "dump", "restore", "destroy", "undestroy", "purge", "status", "host-info", "version",
    ];

    // Yalnızca alan adı, digest'li imaj, döküm adı ve bayrak karakterleri.
    [GeneratedRegex(@"^[A-Za-z0-9._:/@-]{1,200}$")]
    private static partial Regex SafeArg();

    [GeneratedRegex(@"^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex OperationIdPattern();
}

// Gerçek ajan: SSH anahtarıyla bağlanır, sunucunun anahtar parmak izi sabittir (TOFU yok).
// authorized_keys'teki command="..." kısıtı sayesinde gönderilen metin ajanın argümanlarıdır.
public class SshHostAgent(IConfiguration config, ILogger<SshHostAgent> logger) : IHostAgent
{
    public async Task<AgentResult> RunAsync(ServerHost host, AgentCommand command, CancellationToken cancellationToken)
    {
        var keyPath = config["HostAgent:PrivateKeyPath"] ?? throw new InvalidOperationException("HostAgent:PrivateKeyPath tanımlı değil.");
        var timeout = TimeSpan.FromSeconds(config.GetValue("HostAgent:CommandTimeoutSeconds", 900));
        using var key = new PrivateKeyFile(keyPath);
        using var client = new SshClient(host.Address, host.SshPort, host.SshUser, key);
        client.HostKeyReceived += (_, e) => e.CanTrust = $"SHA256:{e.FingerPrintSHA256}" == host.HostKeyFingerprint;
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(20);

        await client.ConnectAsync(cancellationToken);
        try
        {
            using var ssh = client.CreateCommand(command.Text);
            ssh.CommandTimeout = timeout;
            var execution = ssh.ExecuteAsync(cancellationToken);
            if (command.Stdin is not null)
            {
                await using var input = ssh.CreateInputStream();
                var bytes = Encoding.UTF8.GetBytes(command.Stdin);
                await input.WriteAsync(bytes, cancellationToken);
            }
            await execution;
            var result = new AgentResult(ssh.ExitStatus ?? -1, ssh.Result, ssh.Error);
            // Komut metni loglanır (sır içermez: sırlar yalnızca stdin'de), çıktı kısaltılır.
            logger.LogInformation("Ajan {Host}: {Command} -> {Exit}", host.Name, command.Text, result.ExitCode);
            return result;
        }
        finally
        {
            client.Disconnect();
        }
    }
}

// Geliştirme ve test ajanı: sunucu yokken platformun tüm akışı çalışsın. Gerçek ajanın sözleşmesini
// taklit eder (--op tekrarında aynı sonuç, kurulumu tamamlanmamış okul hemen silinir vb.).
public class FakeHostAgent : IHostAgent
{
    private readonly ConcurrentDictionary<string, AgentResult> _operations = new();
    private readonly ConcurrentQueue<string> _calls = new();
    private readonly ConcurrentDictionary<string, int> _failures = new();

    public IReadOnlyList<string> Calls => _calls.ToList();
    public IReadOnlyList<string> Stdins => _stdins.ToList();
    private readonly ConcurrentQueue<string> _stdins = new();

    // Testler: "upgrade" gibi bir fiil, verilen sayı kadar başarısız olsun.
    public void FailNext(string verb, int times = 1) => _failures[verb] = times;

    public Task<AgentResult> RunAsync(ServerHost host, AgentCommand command, CancellationToken cancellationToken)
    {
        var parts = command.Text.Split(' ');
        var opIndex = Array.IndexOf(parts, "--op");
        var op = opIndex >= 0 ? parts[opIndex + 1] : null;
        if (op is not null && _operations.TryGetValue(op, out var recorded)) return Task.FromResult(recorded);

        _calls.Enqueue($"{host.Name}: {command.Text}");
        if (command.Stdin is not null) _stdins.Enqueue(command.Stdin);
        var verb = parts[0] == "route" ? $"route {parts[1]}" : parts[0];

        AgentResult result;
        if (_failures.TryGetValue(verb, out var left) && left > 0)
        {
            _failures[verb] = left - 1;
            result = new AgentResult(1, "", $"sahte hata: {verb}");
        }
        else
        {
            result = verb switch
            {
                "host-info" => new AgentResult(0, "{\"agent\":\"1\",\"memTotalMb\":65536,\"memAvailableMb\":60000,\"diskFreeMb\":400000,\"schools\":0}", ""),
                "version" => new AgentResult(0, "1", ""),
                "provision" => new AgentResult(0, "{\"alreadyProvisioned\":false}", ""),
                "status" => new AgentResult(0, $"{{\"slug\":\"{parts[1]}\",\"state\":\"running\",\"healthy\":true}}", ""),
                "dump" => new AgentResult(0, $"/srv/abdera/{parts[1]}/dumps/{parts[1]}-fake.sql.gz.enc", ""),
                _ => new AgentResult(0, "", ""),
            };
        }

        if (op is not null) _operations[op] = result;
        return Task.FromResult(result);
    }
}
