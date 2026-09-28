namespace Abdera.Api.Modules.Billing.Domain;

// Öğretmen ödeme haftası: PAZARTESİ dahil → CUMARTESİ dahil (okulun yerel saati,
// docs/10-decisions.md O1). Kullanıcı kuralı: "her cumartesi tamamlanan derslerin ödemesini
// yapıyorum" + "pazartesi dahil cumartesi dahil şeklinde olmalı". Ödeme günü haftanın kapandığı
// cumartesidir; gider de o tarihe yazılır, böylece hafta iki ayı bölse bile giderin hangi aya
// düştüğü tek bir tarihle bellidir.
//
// Pazar hiçbir ödeme haftasına girmez - Öğretmenler listesindeki Pzt-Cmt ders/tamamlanan
// sayılarıyla (Benchmark.cs) birebir aynı pencere. Pazar günü işlenmiş bir ders ödenmez; okul
// pazar ders vermeye başlarsa bu karar yeniden açılmalı.
public readonly record struct TeacherPayWeek(DateOnly Start, DateOnly End)
{
    // Verilen günü içeren ödeme haftası. Hafta başı her zaman Pazartesi'ye normalize edilir -
    // (teacher_id, week_start) tekillik kısıtı ancak kanonik bir başlangıçla çift ödemeyi
    // engelleyebilir. Pazar, yeni başlayacak haftaya değil az önce kapanan haftaya bağlanır
    // (pazar günü açılan ekran/form dünkü cumartesinin haftasını göstersin).
    public static TeacherPayWeek Containing(DateOnly date)
    {
        var start = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        return new TeacherPayWeek(start, start.AddDays(5));
    }

    // Haftanın bittiği günün ertesi (pazar): [Start, ExclusiveEnd) yarı açık aralık, ders
    // sayarken Cumartesi 23:59'daki bir dersin dışarıda kalmaması için.
    public DateOnly ExclusiveEnd => End.AddDays(1);

    public TeacherPayWeek Previous() => new(Start.AddDays(-7), End.AddDays(-7));

    public TeacherPayWeek Next() => new(Start.AddDays(7), End.AddDays(7));
}
