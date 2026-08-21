using System.Security.Cryptography;

namespace Abdera.Api.Shared;

// Modules/People/Features/GuardianAuth.cs tarafından kullanılır. TemporaryPasswordGenerator'dan
// ayrı tutulur çünkü OTP telefon tuş takımıyla girilecek - yalnızca rakam, sabit 6 hane.
public static class OtpGenerator
{
    public static string Generate(int length = 6)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = (char)('0' + bytes[i] % 10);
        }
        return new string(chars);
    }
}
