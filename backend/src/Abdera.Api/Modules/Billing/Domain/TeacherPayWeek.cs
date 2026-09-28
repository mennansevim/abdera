namespace Abdera.Api.Modules.Billing.Domain;

// Öğretmen ödeme haftası. Kullanıcı kuralı: "her cumartesi tamamlanan derslerin ödemesini
// yapıyorum" - bu yüzden hafta CUMARTESİ kapanır: Pazar 00:00'dan Cumartesi gece yarısına
// kadar (okulun yerel saati, docs/10-decisions.md N1). Ödeme günü haftanın kapandığı
// Cumartesi'dir; gider de o tarihe yazılır, böylece hafta iki ayı bölse bile giderin hangi
// aya düştüğü tek bir tarihle bellidir.
//
// Uygulamanın geri kalanındaki Pazartesi-başlangıçlı hafta (StudentWeeklyLessonPolicy,
// takvim ekranı) ile karıştırılmamalı: orada hafta gösterim/kota içindir, burada haftanın
// sınırı hangi derslerin ödendiğini belirler. İki kavram bilerek ayrı tutuldu.
public readonly record struct TeacherPayWeek(DateOnly Start, DateOnly End)
{
    // Verilen günü içeren ödeme haftası. Hafta başı her zaman Pazar'a normalize edilir -
    // (teacher_id, week_start) tekillik kısıtı ancak kanonik bir başlangıçla çift ödemeyi
    // engelleyebilir.
    public static TeacherPayWeek Containing(DateOnly date)
    {
        var start = date.AddDays(-(int)date.DayOfWeek);
        return new TeacherPayWeek(start, start.AddDays(6));
    }

    // Haftanın bittiği günün ertesi: [Start, ExclusiveEnd) yarı açık aralık, ders sayarken
    // Cumartesi 23:59'daki bir dersin dışarıda kalmaması için.
    public DateOnly ExclusiveEnd => End.AddDays(1);

    public TeacherPayWeek Previous() => new(Start.AddDays(-7), End.AddDays(-7));

    public TeacherPayWeek Next() => new(Start.AddDays(7), End.AddDays(7));
}
