namespace Abdera.Api.Modules.Assistant.Domain;

// Ders taşıma kapsamı: kullanıcı söylemediyse Unspecified ve kart sorar.
public enum RescheduleScope
{
    Unspecified,
    Once,      // "bu seferlik", "yalnız bu ders"
    Recurring, // "her hafta", "bundan sonra"
}
