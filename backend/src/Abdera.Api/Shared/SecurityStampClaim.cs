namespace Abdera.Api.Shared;

// Cookie oturumuna gömülen ve her istekte User.SecurityStamp/Guardian.SecurityStamp ile
// karşılaştırılan opak değerin claim adı - bkz. Program.cs OnValidatePrincipal,
// Modules/Auth/Features/Login.cs, Modules/People/Features/GuardianAuth.cs,
// Modules/Auth/Features/Logout.cs. Tek yerden tanımlanır ki dört yer de senkron kalsın.
public static class SecurityStampClaim
{
    public const string ClaimType = "security_stamp";
}
