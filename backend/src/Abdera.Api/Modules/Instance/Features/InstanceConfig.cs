using Abdera.Api.Modules.Instance.Domain;
using Abdera.Api.Shared;
using Abdera.Api.Shared.Branches;
using Microsoft.EntityFrameworkCore;
using Abdera.Api.Shared.Modules;

namespace Abdera.Api.Modules.Instance.Features;

// Arayüzün açılışta okuduğu kurulum bilgisi: okulun kimliği ve açık modüller. Giriş ekranı da
// okul adını/logosunu göstermek için çağırdığından anonimdir; kişisel veri içermez.
public static class InstanceConfig
{
    public record ModuleResponse(string Key, string Name, bool IsCore, bool Enabled, IReadOnlyList<string> Requires);
    public record ProfileResponse(string Name, string ShortName, string? PrimaryColor, string? LogoUrl, string? LegalName, string? ContactEmail);
    // Okul günü saatleri gizli değil ve takvim ekranları açılışta ihtiyaç duyuyor (school_policy).
    public record SchoolDayResponse(TimeOnly Start, TimeOnly End);
    // Demo: tanıtım yayınında giriş ekranı örnek hesapları doldurur. Eskiden NEXT_PUBLIC_DEMO_ENABLED
    // ile derleme anında gömülüyordu; tek ön yüz imajı her okulda çalışsın diye çalışma anına taşındı (A10).
    // Alt birim terimleri (BranchCatalog.TermsFor): tek kategorili okulda "Enstrüman", karışıkta "Branş".
    public record TermsResponse(string Branch, string BranchPlural);
    public record ConfigResponse(
        ProfileResponse Profile, IReadOnlyList<ModuleResponse> Modules, SchoolDayResponse SchoolDay, bool Demo,
        TermsResponse Terms, IReadOnlyList<People.Features.BranchCategories.CategoryResponse> Categories);

    public static void MapInstanceConfig(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/instance/config", GetAsync).AllowAnonymous();
    }

    private static async Task<IResult> GetAsync(HttpContext http, AbderaDbContext db, IConfiguration config, EnabledModules modules)
    {
        var profile = await SchoolProfile.GetCurrentAsync(db);
        var policy = await SchoolPolicy.GetCurrentAsync(db, config);
        var enabled = modules.Current;
        var categories = await People.Features.BranchCategories.ResponsesAsync(db);
        var (branch, branchPlural) = BranchCatalog.TermsFor(
            categories.Where(c => c.InUse).Select(c => c.Key).ToList(),
            categories.Select(c => new BranchCategory(c.Key, c.Name, c.UnitLabel, c.UnitLabelPlural, c.Suggestions)).ToList());

        // Okul adı/renk/modül değişikliği bir sonraki sayfa açılışında hemen yansısın.
        http.Response.Headers.CacheControl = "no-cache";
        return Results.Ok(new ConfigResponse(
            ToProfileResponse(profile),
            ModuleCatalog.All
                .Select(module => new ModuleResponse(
                    module.Key, module.Name, module.IsCore, enabled.Contains(module.Key), module.Requires))
                .ToList(),
            new SchoolDayResponse(policy.SchoolDayStart, policy.SchoolDayEnd),
            config.GetValue<bool>("Demo:Enabled"),
            new TermsResponse(branch, branchPlural),
            categories));
    }

    public static ProfileResponse ToProfileResponse(SchoolProfile profile) => new(
        profile.Name,
        profile.ShortName,
        profile.PrimaryColor,
        profile.LogoVersion is { } version ? $"/api/instance/logo?v={version:N}" : null,
        profile.LegalName,
        profile.ContactEmail);
}
