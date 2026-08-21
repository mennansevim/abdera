namespace Abdera.Api.Modules.Auth.Domain;

// docs/00-master-prompt.md - initial roles were Admin/Teacher only (Guardian had no account,
// WhatsApp only). docs/10-decisions.md Karar F bunu kısmen tersine çevirdi (veli için OTP ile
// giriş, yalnızca kendi RSVP'sini/takvimini görmek için) - bkz. Modules/People/Features/GuardianAuth.cs.
// Guardian değeri yalnızca claim/policy string'idir; `users` tablosunda hiçbir zaman bir
// Guardian satırı olmaz, oturumu doğrudan Guardian.Id üzerinden kurulur.
public enum UserRole
{
    Admin,
    Teacher,
    Guardian,
}
