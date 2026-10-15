namespace Abdera.Platform.Api.Domain;

public enum HostStatus
{
    Active,
    Draining,
    Offline,
}

// Okul sunucusu (§3.2 hosts). Kapasite okul başına rezerv bellekle izlenir; iki kurulum aynı
// son yeri alamasın diye xmin ile korunur (§6 adım 2).
public class ServerHost
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public int SshPort { get; private set; }
    public string SshUser { get; private set; } = null!;
    // SHA256 parmak izi (ssh-keygen -lf biçimindeki base64 kısmı). Bağlantıda birebir eşleşmeli.
    public string HostKeyFingerprint { get; private set; } = null!;
    public string PublicIp { get; private set; } = null!;
    public int TotalMemoryMb { get; private set; }
    public int ReservedMemoryMb { get; private set; }
    public HostStatus Status { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public string? AgentVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint RowVersion { get; private set; }

    private ServerHost() { }

    public static ServerHost Create(
        string name, string address, int sshPort, string sshUser, string hostKeyFingerprint, string publicIp,
        int totalMemoryMb, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60) errors["name"] = ["Ad 1-60 karakter olmalı."];
        if (string.IsNullOrWhiteSpace(address) || address.Length > 253) errors["address"] = ["Adres gerekli."];
        if (sshPort is < 1 or > 65535) errors["sshPort"] = ["Port 1-65535 olmalı."];
        if (string.IsNullOrWhiteSpace(sshUser)) errors["sshUser"] = ["Kullanıcı gerekli."];
        if (string.IsNullOrWhiteSpace(hostKeyFingerprint) || !hostKeyFingerprint.StartsWith("SHA256:", StringComparison.Ordinal))
            errors["hostKeyFingerprint"] = ["Parmak izi 'SHA256:...' biçiminde olmalı (ssh-keygen -lf)."];
        if (!System.Net.IPAddress.TryParse(publicIp, out _)) errors["publicIp"] = ["Geçerli bir IP adresi girilmeli."];
        if (totalMemoryMb < 1024) errors["totalMemoryMb"] = ["Bellek en az 1024 MB olmalı."];
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        return new ServerHost
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Address = address.Trim(),
            SshPort = sshPort,
            SshUser = sshUser.Trim(),
            HostKeyFingerprint = hostKeyFingerprint.Trim(),
            PublicIp = publicIp.Trim(),
            TotalMemoryMb = totalMemoryMb,
            Status = HostStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    // %20 pay sistem ve ani yük için ayrılır (§10).
    public int CapacityMb => (int)(TotalMemoryMb * 0.8);
    public bool CanFit(int memoryMb) => Status == HostStatus.Active && ReservedMemoryMb + memoryMb <= CapacityMb;

    public void Reserve(int memoryMb, DateTimeOffset now)
    {
        if (!CanFit(memoryMb)) throw new ConflictException($"{Name} sunucusunda yer yok.");
        ReservedMemoryMb += memoryMb;
        UpdatedAt = now;
    }

    public void Release(int memoryMb, DateTimeOffset now)
    {
        ReservedMemoryMb = Math.Max(0, ReservedMemoryMb - memoryMb);
        UpdatedAt = now;
    }

    public void RecordCheck(string agentVersion, DateTimeOffset now)
    {
        AgentVersion = agentVersion;
        LastCheckedAt = now;
        UpdatedAt = now;
    }

    public void SetStatus(HostStatus status, DateTimeOffset now) { Status = status; UpdatedAt = now; }
}
