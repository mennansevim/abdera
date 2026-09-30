// Kullanıcıya gösterilen sürüm notları - her canlıya alış bir sürümdür (docs/10-decisions.md U1).
// abdera-deploy skill'i deploy'dan ÖNCE en üste yeni bir kayıt ekler; en üstteki kayıt canlıdaki
// sürümdür. Sıra dizideki konumdur (en yeni önce), sürüm metni karşılaştırılmaz.
//
// Sürüm: canlıya alış günü `YYYY.MM.DD`; aynı gün ikinci alış `YYYY.MM.DD.2`.
// Maddeler kullanıcının diliyle yazılır (commit mesajı değil): ekranda ne değişti, kime ne
// kazandırır. `audience: "admin"` maddesini öğretmen görmez. Kullanıcıya görünen değişiklik
// yoksa `items` boş kalır - sürüm yine kaydedilir ama "Yenilikler" penceresi açılmaz.

export type ReleaseAudience = "all" | "admin";

export interface ReleaseItem {
  audience: ReleaseAudience;
  text: string;
}

export interface Release {
  version: string;
  date: string;
  items: ReleaseItem[];
}

export const RELEASES: Release[] = [
  {
    version: "2026.09.30",
    date: "2026-09-30",
    items: [
      { audience: "all", text: "Her güncellemeden sonra bu pencere açılır ve nelerin değiştiğini özetler. Geçmiş sürüm notlarına menünün altındaki sürüm numarasından ulaşabilirsin." },
      { audience: "all", text: "Veli bir derse \"gelemiyor\" ya da \"gecikecek\" derse dersin öğretmenine bildirim gelir; takvimde o ders kırmızı/sarı ünlemle işaretlenir." },
      { audience: "all", text: "Seriye bağlı bir dersi iptal ederken yalnızca o dersi mi yoksa tüm seriyi mi iptal edeceğini seçebilirsin. İptal edilen dersler takvimde artık görünmez." },
      { audience: "all", text: "Öğretmenler de takvimdeki \"Telafi planla\" düğmesiyle telafi dersi planlayabilir." },
      { audience: "all", text: "Öğretmen çalışma saatleri 09:00–21:00 aralığında seçilebilir." },
      { audience: "admin", text: "Ana ekranda haftanın özeti: günlere göre toplam ders, enstrümana ve öğretmene göre ders sayısı, olumsuz veli yanıtları. Sağ panel kapalı başlar, bekleyen iş varsa rozetle gösterir." },
      { audience: "admin", text: "Kurs kaydında enstrüman alt dalı seçilebilir (ör. Gitar: Elektro / Bas). Öğrenci künyesinden yeniden kurs eklenebiliyor." },
      { audience: "admin", text: "Yeni veliye karşılama mesajı gider; veli portalı şifresi WhatsApp'tan gönderilmez." },
      { audience: "admin", text: "Mesaj Merkezi'ndeki gönderimler planlanan tarihe göre yeniden eskiye sıralanır; iptal edilenler en sonda." },
    ],
  },
];
