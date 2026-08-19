using System.Security.Cryptography;

namespace Abdera.Api.Shared;

// Auth/ResetPassword ve People/CreateTeacher tarafından paylaşılır - biri diğerinin
// kopyası olmasın diye tek yerden üretilir (CLAUDE.md: duplicated business rules yok).
public static class TemporaryPasswordGenerator
{
    // Okunması kolay, karışıklık yaratan karakterler (0/O, 1/l) hariç - telefonla iletilecek.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";

    public static string Generate(int length = 12)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }
        return new string(chars);
    }
}
