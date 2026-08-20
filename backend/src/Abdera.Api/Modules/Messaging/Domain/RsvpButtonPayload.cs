using System.Security.Cryptography;
using System.Text;

namespace Abdera.Api.Modules.Messaging.Domain;

// docs/06-whatsapp.md "Buton payload güvenliği": "Payload'da tahmin edilebilir dahili id
// kullanılmaz... imzalı/opak referans." X-Hub-Signature-256 yalnızca isteğin gerçekten
// Meta'dan geldiğini kanıtlar - bu, WhatsApp__PayloadSigningKey ile AYRICA imzalanan buton
// verisinin bizim ürettiğimiz haliyle (değiştirilmeden) geri geldiğini kanıtlar. Lesson id
// zaten UUID (tahmin edilemez, CLAUDE.md), ama imza olmadan biri başka bir dersin id'sini
// bilip payload'ı elle kurabilir - imza bunu engeller.
public static class RsvpButtonPayload
{
    public const string AttendingAction = "rsvp_attending";
    public const string NotAttendingAction = "rsvp_not_attending";

    public static string Sign(string action, Guid lessonId, string signingKey)
    {
        var referenceToken = Convert.ToBase64String(lessonId.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var signature = ComputeSignature(action, referenceToken, signingKey);
        return $"{action}:{referenceToken}.{signature}";
    }

    public static bool TryVerify(string payload, string signingKey, out string action, out Guid lessonId)
    {
        action = "";
        lessonId = Guid.Empty;

        var colonIndex = payload.IndexOf(':');
        if (colonIndex < 0) return false;

        action = payload[..colonIndex];
        var rest = payload[(colonIndex + 1)..];
        var dotIndex = rest.IndexOf('.');
        if (dotIndex < 0) return false;

        var referenceToken = rest[..dotIndex];
        var signature = rest[(dotIndex + 1)..];

        var expectedSignature = ComputeSignature(action, referenceToken, signingKey);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(expectedSignature)))
            return false;

        try
        {
            var base64 = referenceToken.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            lessonId = new Guid(Convert.FromBase64String(base64));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ComputeSignature(string action, string referenceToken, string signingKey)
    {
        var data = Encoding.UTF8.GetBytes($"{action}:{referenceToken}");
        var key = Encoding.UTF8.GetBytes(signingKey);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
