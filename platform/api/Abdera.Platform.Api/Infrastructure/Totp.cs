using System.Security.Cryptography;

namespace Abdera.Platform.Api.Infrastructure;

// RFC 6238 TOTP (30 sn, 6 hane, HMAC-SHA1) - kimlik doğrulayıcı uygulamalarla uyumlu. Ek paket yok.
public static class Totp
{
    private const int StepSeconds = 30;

    public static string NewSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static string ProvisioningUri(string secret, string account) =>
        $"otpauth://totp/{Uri.EscapeDataString("Miredo:" + account)}?secret={secret}&issuer={Uri.EscapeDataString("Miredo")}&digits=6&period=30";

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string CodeAt(string secret, long step)
    {
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(Base32Decode(secret), counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    // ±1 adım saat kayması tolere edilir. Dönen adım kaydedilir; daha eski/aynı adım tekrar kabul edilmez.
    public static long? Verify(string secret, string? code, DateTimeOffset now, long lastAcceptedStep)
    {
        if (code is null || code.Length != 6 || !code.All(char.IsAsciiDigit)) return null;
        var current = StepAt(now);
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (step <= lastAcceptedStep) continue;
            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(CodeAt(secret, step)), System.Text.Encoding.ASCII.GetBytes(code)))
                return step;
        }
        return null;
    }

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static string Base32Encode(byte[] data)
    {
        var output = new System.Text.StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException("Geçersiz base32.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }
}
