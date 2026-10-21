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
        app.MapUpdateDisplayName();
        app.MapResetPassword();
        app.MapStaffSeats();

        // Build() sonrası okunduğu için WebApplicationFactory override'ları da görülür.
        if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Auth:DevLogin:Enabled", false))
        {
            app.MapDevLogin();
        }
    }
}
