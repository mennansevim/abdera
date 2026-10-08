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
    version: "2026.10.08.5",
    date: "2026-10-08",
    items: [
      { audience: "all", text: "Bugün ekranındayken köşede bildirim kartı açılmaz; yeni bildirimler zilde sayılır ve başka bir ekrana geçince kart olarak görünür." },
      { audience: "all", text: "Bir dersin notunu yazınca zildeki \"yorumu bekliyor\" hatırlatması hemen güncellenir; notu girilmiş dersler artık listede sayılmaz." },
    ],
  },
  {
    version: "2026.10.08.4",
    date: "2026-10-08",
    items: [
      { audience: "all", text: "\"Yorum bekleyen dersler\" listesi ana ekrandan kalktı; ana ekran doğrudan günün dersleriyle açılır. Liste artık öğretmenin Gelişim ekranının başında, zildeki hatırlatma da oraya götürür." },
      { audience: "all", text: "Öğretmenler yanlış yazdıkları bir ders notunu Gelişim ekranında \"Notu sil\" ile kalıcı olarak silebilir." },
      { audience: "admin", text: "Giderler ekranı \"Gelir ve gider\" oldu: Özet sekmesi aylara göre gelir, gider ve nakit akışını gösterir; Gelirler sekmesinde tahsil edilen aidatlar ödeme yöntemine göre listelenir." },
    ],
  },
  {
    version: "2026.10.08.3",
    date: "2026-10-08",
    items: [
      { audience: "all", text: "Ders notu formu sadeleşti: \"Çalınan eser\" alanı kalktı. \"Ders devam ediyor\" artık önceki dersin \"Ne çalışıldı?\" bilgisini, ödevini ve hedefini taşır." },
      { audience: "all", text: "Bir bildirim köşede kart olarak yalnızca bir kez açılır; tekrar hatırlatıldığında yalnızca zildeki sayıda görünür." },
    ],
  },
  {
    version: "2026.10.08.2",
    date: "2026-10-08",
    items: [
      { audience: "admin", text: "Müsaitlik ekranında bir saate dokununca her öğretmenin haftanın tüm boş aralıkları gün gün listelenir (ör. Per 15:00–21:00); bir aralığa dokunmak kayıt formunu o saatle açar. Doluluk yüzdesinin yerine velinin tercihine uyan boş süre yazar." },
    ],
  },
  {
    version: "2026.10.08",
    date: "2026-10-08",
    items: [
      { audience: "all", text: "Ders notu yazarken \"Ders devam ediyor\" düğmesi, öğrencinin bir önceki dersindeki eseri, ödevi ve hedefi forma tek dokunuşla taşır; aynı esere devam ettiğinde yeniden yazman gerekmez." },
      { audience: "all", text: "Not formunda hızlı not düğmeleri var: \"Verimli bir ders oldu\", \"Ödevini yapmış\" gibi cümleleri ve gam, etüt, ritim gibi çalışılan konuları dokunarak ekleyip çıkarabilirsin." },
      { audience: "all", text: "Notu girilmemiş dersler için zildeki hatırlatma artık her gün değil, üç günde bir gelir. Ana ekrandaki \"Yorum bekleyen dersler\" kartı her zamanki gibi görünür." },
      { audience: "admin", text: "Yeni Müsaitlik ekranı: yeni bir veli geldiğinde enstrümana, ders süresine ve velinin tercihine göre hangi öğretmenin hangi saatte her hafta boş olduğunu tek bakışta görürsün. Bir saate dokununca boş öğretmenler listelenir; \"Kayıt aç\" yeni öğrenci formunu öğretmen, gün ve saat dolu olarak açar." },
    ],
  },
  {
    version: "2026.10.06",
    date: "2026-10-06",
    items: [
      { audience: "all", text: "Oturumun süresi dolduğunda uygulama artık seni hemen giriş ekranına alır ve nedenini yazar. Böylece \"Geldi\" gibi düğmeler sessizce çalışmaz hale gelmez; tekrar giriş yapıp kaldığın yerden devam edersin." },
    ],
  },
  {
    version: "2026.10.02.2",
    date: "2026-10-02",
    items: [
      { audience: "admin", text: "Aidatlar listesinde tahsilat düğmesinin adı \"Ödeme al\" oldu. Yeşil artık yalnızca ödemesi alınmış öğrencilerin \"✓ Ödendi\" rozetinde görünür." },
    ],
  },
  {
    version: "2026.10.02",
    date: "2026-10-02",
    items: [
      { audience: "admin", text: "Aidatlar ekranı sadeleşti: üstteki Liste | Çizelge düğmesiyle ayın bekleyenlerini ya da tüm yılı öğrenci × ay olarak görürsün. Öğrenci, öğretmen veya veli adıyla arayabilirsin." },
      { audience: "admin", text: "Listede öğrencinin yanındaki \"Ödendi\" ayın aidatını tek dokunuşla kapatır; yanlışlıkla bastıysan birkaç saniye içinde \"Geri al\"." },
      { audience: "admin", text: "İsme ya da çizelgedeki bir aya dokununca tek bir ödeme penceresi açılır: birkaç ay seçip toplu ödeme alabilir, kardeş, çoklu kurs, peşin ve özel indirimi o an açıp kapatabilirsin. Kısmi ödeme ve geçmiş ödemeler de buradadır." },
      { audience: "all", text: "Yoklamada \"Mazeretli\" işaretlenen ders öğrenciye telafi hakkı verir; telafi dersi ders gününden sonraki 21 gün içinde takvimden planlanabilir." },
      { audience: "all", text: "Öğrenciler listesinde her öğrencinin son 4 haftalık yoklaması rozetlerle görünür." },
    ],
  },
  {
    version: "2026.10.01",
    date: "2026-10-01",
    items: [
      { audience: "all", text: "Giriş ekranında Yöneticiyim ya da Öğretmenim'i seçince o rolle bu cihazda en son kullanılan e-posta hazır gelir; telefonun kayıtlı şifresi de önerilir." },
      { audience: "all", text: "Haftalık ders programı yeni bir gün ya da saate taşınınca veliye WhatsApp'tan haber verilir; dersin öğretmeni ve yöneticiler ekran içi bildirim alır." },
      { audience: "all", text: "Ders taşıma penceresinde kaydederken yükleniyor göstergesi çıkar; taşıma olmazsa nedeni pencerenin içinde açıkça yazar." },
      { audience: "admin", text: "Gelişim günlüğünde önce öğretmeni seçip yalnızca onun öğrencileri arasında gezinebilirsin." },
      { audience: "admin", text: "Ana ekrandaki günün özetinden vadesi geçen aidat kartı kaldırıldı; geciken aidatlar Aidatlar ekranında görünmeye devam ediyor." },
    ],
  },
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
