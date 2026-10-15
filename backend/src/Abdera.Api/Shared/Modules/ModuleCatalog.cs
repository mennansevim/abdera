namespace Abdera.Api.Shared.Modules;

// docs/multi-tenant/ARCHITECTURE.md §5.2: her okul kurulumu hangi modülleri kullanacağını seçer.
// Katalog TEK kaynaktır - platform paneli de aynı listeyi /api/instance/config üzerinden okur,
// böylece iki uygulama "hangi modül var, neye bağlı" konusunda ayrışmaz.
//
// Şema her okulda aynıdır: kapalı modülün tabloları boş durur, modülü sonradan açmak migration
// gerektirmez, kapatmak veriyi silmez - yalnızca uç noktalar 404 döner ve menüde görünmez.
public static class ModuleKeys
{
    public const string People = "core.people";
    public const string Scheduling = "core.scheduling";
    public const string Attendance = "core.attendance";
    public const string Billing = "core.billing";
    public const string Notifications = "core.notifications";
    public const string Ops = "core.ops";

    public const string Progress = "progress";
    public const string Library = "library";
    public const string Show = "show";
    public const string Banking = "banking";
    public const string Profitability = "profitability";
    public const string ParentPortal = "parent-portal";
    public const string Groups = "groups";
    public const string Finance = "finance";
}

public sealed record ModuleDefinition(string Key, string Name, bool IsCore, IReadOnlyList<string> Requires);

public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDefinition> All =
    [
        new(ModuleKeys.People, "Öğrenci, veli, öğretmen", true, []),
        new(ModuleKeys.Scheduling, "Takvim ve seri ders", true, [ModuleKeys.People]),
        new(ModuleKeys.Attendance, "Yoklama ve telafi", true, [ModuleKeys.Scheduling]),
        new(ModuleKeys.Billing, "Aidat ve tahsilat", true, [ModuleKeys.People]),
        new(ModuleKeys.Notifications, "Ekran içi bildirimler", true, []),
        new(ModuleKeys.Ops, "Sağlık ve yedek", true, []),

        new(ModuleKeys.Progress, "Gelişim ve karne", false, [ModuleKeys.Scheduling]),
        new(ModuleKeys.Library, "Kütüphane", false, []),
        new(ModuleKeys.Show, "Yıl sonu gösterisi", false, [ModuleKeys.People]),
        new(ModuleKeys.Banking, "Banka entegrasyonu", false, [ModuleKeys.Billing]),
        new(ModuleKeys.Groups, "Grup dersleri", false, [ModuleKeys.Scheduling, ModuleKeys.Billing]),
        new(ModuleKeys.Finance, "Gelir-gider ve öğretmen ödemeleri", false, [ModuleKeys.Billing]),
        // Kârlılık ders başı maliyeti öğretmen ücretinden, sabit gideri gider tablosundan okur.
        new(ModuleKeys.Profitability, "Kârlılık", false, [ModuleKeys.Finance]),
        new(ModuleKeys.ParentPortal, "Veli portalı", false, [ModuleKeys.People, ModuleKeys.Billing]),
    ];

    public static ModuleDefinition? Find(string key) =>
        All.FirstOrDefault(module => string.Equals(module.Key, key, StringComparison.OrdinalIgnoreCase));

    // "Hiçbir opsiyonel modül seçilmedi" boş değerle yazılamaz (boş = eski kurulum, hepsi açık);
    // platform sihirbazı bu durumda açıkça bu değeri yazar.
    public const string NoneSentinel = "none";

    // Modules:Enabled boş/tanımsızsa TÜM modüller açıktır - bugünkü tek okullu kurulumun davranışı
    // birebir korunur. "none" yalnızca çekirdek modülleri açar. Doluysa virgülle ayrılmış opsiyonel
    // modül anahtarlarıdır; çekirdek modüller listede olmasa da her zaman açıktır.
    public static IReadOnlySet<string> ResolveEnabled(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return All.Select(module => module.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (string.Equals(configured.Trim(), NoneSentinel, StringComparison.OrdinalIgnoreCase))
        {
            return All.Where(module => module.IsCore).Select(module => module.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var requested = configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var unknown = requested.Where(key => Find(key) is null).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"Modules:Enabled bilinmeyen modül içeriyor: {string.Join(", ", unknown)}. " +
                $"Geçerli opsiyonel modüller: {string.Join(", ", All.Where(m => !m.IsCore).Select(m => m.Key))}.");
        }

        var enabled = All.Where(module => module.IsCore).Select(module => module.Key)
            .Concat(requested.Select(key => Find(key)!.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Bağımlılığı kapalı bir modül sessizce yarım çalışmasın: kurulum açık bir hatayla durur.
        var missing = enabled
            .SelectMany(key => Find(key)!.Requires.Where(required => !enabled.Contains(required))
                .Select(required => $"{key} → {required}"))
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Modules:Enabled bağımlılığı eksik modül içeriyor: {string.Join(", ", missing)}.");
        }

        return enabled;
    }
}
