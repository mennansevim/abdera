namespace Abdera.Api.Modules.Progress.Infrastructure;

// Ses Defteri dosyaları için düz disk deposu (issue #52). Production'da ayrı bir Docker volume'e
// bağlanır (docker-compose.yml `abdera_audio`), böylece günlük pg_dump'a girmez; yedeği
// AudioClipMaintenance yalnızca yeni dosyaları şifreleyip yükleyerek alır.
//
// Dosya adı = kayıt UUID'si + uzantı ve dosya hiç değişmez: üzerine yazma, sürümleme yok. Bu da
// tarayıcı önbelleğinin "immutable" sunulabilmesini ve artımlı yedeğin basit kalmasını sağlar.
public class AudioClipStore(IConfiguration config, IWebHostEnvironment environment)
{
    // Deferred okuma: WebApplicationFactory'nin test-time config'i ancak Build()'den sonra
    // görünür (CLAUDE.md Program.cs kuralı). Singleton olduğu için ilk erişimde bir kez çözülür.
    private string? _root;

    public string Root => _root ??= ResolveRoot();

    public async Task SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        var path = PathFor(fileName);
        Directory.CreateDirectory(Root);
        // Önce geçici ada yazılıp sonra taşınır: yarım yazılmış bir dosya hiçbir zaman gerçek
        // adıyla görünmez (sunum ve yedek onu okumaz).
        var temp = path + ".part";
        await File.WriteAllBytesAsync(temp, content, cancellationToken);
        File.Move(temp, path, overwrite: false);
    }

    public string? ExistingPath(string fileName)
    {
        var path = PathFor(fileName);
        return File.Exists(path) ? path : null;
    }

    public void Delete(string fileName)
    {
        var path = PathFor(fileName);
        if (File.Exists(path)) File.Delete(path);
    }

    public void DeleteMany(IEnumerable<string> fileNames)
    {
        foreach (var fileName in fileNames) Delete(fileName);
    }

    // Diskteki dosyalar (yarım kalmış .part'lar dahil) - yetim temizliği bunlardan veritabanında
    // karşılığı olmayanları siler.
    public IEnumerable<FileInfo> ListFiles() =>
        Directory.Exists(Root) ? new DirectoryInfo(Root).EnumerateFiles() : [];

    public (long Bytes, int Files) Usage()
    {
        long bytes = 0;
        var files = 0;
        foreach (var file in ListFiles())
        {
            bytes += file.Length;
            files++;
        }
        return (bytes, files);
    }

    private string PathFor(string fileName)
    {
        // Dosya adı her zaman sunucuda üretilir (AudioClip.Create); yine de yol dışına çıkmayı
        // engelleyen tek satırlık bir sigorta.
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
            throw new InvalidOperationException("Geçersiz ses dosyası adı.");
        return Path.Combine(Root, fileName);
    }

    private string ResolveRoot()
    {
        var configured = config["AudioClips:Directory"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "data", "audio-clips")
            : configured;
    }
}
