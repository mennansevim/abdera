using Abdera.Api.Modules.Instance.Features;

namespace Abdera.Api.Modules.Instance;

// Bu okul kurulumunun kimliği ve açık modülleri (docs/multi-tenant/ARCHITECTURE.md §5).
// Okul başına bağımsız kurulum modelinde her kurulum kendi profilini ve modül listesini taşır.
public static class InstanceModule
{
    public static void MapInstanceModule(this WebApplication app)
    {
        app.MapInstanceConfig();
        app.MapSchoolProfile();
        app.MapSchoolPolicy();
    }
}
