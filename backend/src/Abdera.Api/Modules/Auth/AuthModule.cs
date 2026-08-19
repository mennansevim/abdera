using Abdera.Api.Modules.Auth.Features;

namespace Abdera.Api.Modules.Auth;

// Kompozisyon kökü: modülün tüm endpoint'lerini tek yerden kaydeder.
// Program.cs yalnızca app.MapAuthModule() der, ayrıntı burada.
public static class AuthModule
{
    public static void MapAuthModule(this WebApplication app)
    {
        app.MapLogin();
        app.MapLogout();
        app.MapMe();
        app.MapChangePassword();
        app.MapResetPassword();
    }
}
