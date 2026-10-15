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
    version: "2026.10.15.4",
    date: "2026-10-15",
    items: [],
  },
  {
    version: "2026.10.15.3",
    date: "2026-10-15",
    items: [
      { audience: "all", text: "Ayarlar'da artık kendine bir görünen ad seçebilirsin; menüdeki kullanıcı kartında ve karşılama ekranında bu ad görünür. Öğretmenler ad seçmezse kendi adlarıyla görünür." },
    ],
  },
  {
    version: "2026.10.15.2",
    date: "2026-10-15",
    items: [
      { audience: "admin", text: "Gider eklerken yeni kategoriler var: Reklam, Bakım / akort, Vergi / SGK, Malzeme / kırtasiye ve Enstrüman / ekipman. Giderler ekranında kategori filtresi artık açılır listede; dağılımda yalnızca o dönemde tutarı olan kategoriler görünür." },
      { audience: "admin", text: "Aylık aidat üretimi, WhatsApp gönderimi ve hatırlatmalar gibi otomatik işler artık izleniyor: biri üst üste çalışmazsa ana ekranda uyarı çıkar ve e-posta gelir. Yedekleme ekranında her işin son çalışma zamanı görünür." },
    ],
  },
  {
    version: "2026.10.15",
    date: "2026-10-15",
    items: [
      { audience: "admin", text: "Grup'a daha önce alınmış Resim öğrencilerinin henüz ödenmemiş aidatları da 4.500 TL'ye düzeltildi; ödeme penceresi artık 6.000 TL göstermez. Ödenmiş aylar değişmez." },
      { audience: "all", text: "Günün sözüne kaynağı belli yeni sözler eklendi." },
    ],
  },
  {
    version: "2026.10.14.5",
    date: "2026-10-14",
    items: [
      { audience: "admin", text: "Bir kursun ders türünü (Birebir/Grup) ya da özel indirimini değiştirince, henüz ödenmemiş açık aidatlar da yeni tutara geçer. Örneğin Grup'a alınan Resim öğrencisinin bu ayki aidatı 4.500 TL olur; ödenmiş aylar değişmez." },
      { audience: "all", text: "Ana ekranda tarihin altında her gün değişen bir \"günün sözü\" görünür." },
    ],
  },
  {
    version: "2026.10.14.4",
    date: "2026-10-14",
    items: [
      { audience: "all", text: "Ders penceresinde yeni \"Yer değiştir\" düğmesi: aynı öğretmenin bu haftaki iki dersini tek adımda yer değiştirebilirsiniz (örneğin Perşembe 15:00 ile Cuma 15:00). Her öğrenci kendi ders süresini korur." },
      { audience: "all", text: "Yer değiştirirken \"Yalnız bu hafta\" yalnızca o iki dersi taşır; \"Bundan sonra hep\" iki öğrencinin haftalık programını o haftadan itibaren değiştirir. Velilere yeni saat bildirilir." },
    ],
  },
  {
    version: "2026.10.14.3",
    date: "2026-10-14",
    items: [
      { audience: "admin", text: "Yeni \"Grup dersleri\" ekranı: Resim gibi grup dersleri için öğretmen, haftalık gün ve saat seçip grup açabilir, altına en fazla 6 öğrenci ekleyebilirsiniz. Eklenen öğrencinin Grup aidatı kendiliğinden açılır." },
      { audience: "all", text: "Takvimde grup dersi tek bir \"GRUP\" kartı olarak görünür; karta tıklayınca öğrenci listesi açılır ve her öğrencinin yoklaması ayrı girilir. Grubun saatine başka bir ders konamaz." },
      { audience: "admin", text: "Grup dersi öğretmenine o ayın grup aidatlarının %40'ı ödenir: Gelir ve gider ekranında Gider ekle → \"Aylık grup\" sekmesinden. Grup dersleri haftalık ders ödemesinde artık sayılmaz." },
    ],
  },
  {
    version: "2026.10.14.2",
    date: "2026-10-14",
    items: [
      { audience: "all", text: "Bir bildiriminizi tam anlayamazsak Hata bildir sayfasında size bir soru görünür; cevabınızı oradan yazdığınızda bildiriminiz yeniden incelenir." },
      { audience: "admin", text: "Gelen bildirimlerde yeni \"Beklemede\" sekmesi: anlaşılamayan bir bildirim için \"Detay iste\" ile bildirimi yapana soru sorabilirsiniz." },
    ],
  },
  {
    version: "2026.10.14",
    date: "2026-10-14",
    items: [
      { audience: "admin", text: "Yeni \"Veli girişleri\" ekranı: veliye 6 haneli şifre verebilir, verdiğiniz şifreyi veli kendi şifresini belirleyene kadar tekrar görebilirsiniz. Veli giriş ekranında şifresini bilmeyen veliye okul yönetiminden yeni şifre istemesi söylenir." },
      { audience: "admin", text: "Müsaitlik ekranındaki \"Veliye seçenekleri kopyala\" artık her günün yalnızca ilk boş saatini değil, tüm boş aralıklarını yazar (örneğin \"Cuma 15:00–19:00 arası\")." },
    ],
  },
  {
    version: "2026.10.13.4",
    date: "2026-10-13",
    items: [
      { audience: "all", text: "Planlanmış bir telafi dersinin günü, saati ve süresi ders penceresindeki \"Düzenle\" ile değiştirilebilir; ders telafi dersi olarak kalır ve yeni gün telafi hakkının geçerli olduğu tarihler içinde olmalıdır." },
    ],
  },
  {
    version: "2026.10.13.3",
    date: "2026-10-13",
    items: [
      { audience: "all", text: "Bildirdiğiniz bir hata ya da öneri çözülünce Hata bildir sayfasında görünür; \"Onaylandı\" ya da \"Hâlâ hatalı\" diyerek sonucu bize bildirebilirsiniz." },
      { audience: "admin", text: "Gelen bildirimlerde Görevde ve Çözüldü sekmeleri var; bir bildirimi \"Çözüldü\" olarak işaretleyebilir, kullanıcının teyidini takip edebilirsiniz." },
    ],
  },
  {
    version: "2026.10.13.2",
    date: "2026-10-13",
    items: [
      { audience: "all", text: "Telafi planlarken istediğiniz saat önerilerde yoksa \"Başka bir gün/saat\" alanından gün ve saati kendiniz seçebilirsiniz; saat öğretmenin müsaitliği ve çakışmalarla anında kontrol edilir." },
      { audience: "all", text: "Ana ekranın başlığı sadeleşti: tarihin yanındaki açıklama yazısı kaldırıldı." },
    ],
  },
  {
    version: "2026.10.13",
    date: "2026-10-13",
    items: [],
  },
  {
    version: "2026.10.11.4",
    date: "2026-10-11",
    items: [
      { audience: "all", text: "Bir derse ait bildirime (ders taşındı, iptal, telafi, veli cevabı, \"öğrenci geldi mi?\") dokununca takvimde o dersin penceresi doğrudan açılır; kapatınca takvim o dersin haftasında kalır." },
    ],
  },
  {
    version: "2026.10.11.3",
    date: "2026-10-11",
    items: [
      { audience: "all", text: "Gelişim ekranında yeni \"Dönem karnesi\" paneli: her öğrencinin dönem karnesini açıp yorumunu yazar, \"Yayınla\" dersin. Devam, çalışılan eserler, beceri puanları ve evde çalışma kendiliğinden gelir." },
      { audience: "all", text: "Yayınlanan karne velinin portalında Gelişim sekmesinde görünür; taslak veliye görünmez. Yayından sonra değiştirmek istersen önce yayından kaldırman yeterli." },
      { audience: "admin", text: "Dönem karnesi panelinde okulun tamamında kaç karnenin yayınlandığını, kaçının taslakta beklediğini görürsün; gerekirse karneyi sen de yazıp yayınlayabilirsin." },
    ],
  },
  {
    version: "2026.10.11.2",
    date: "2026-10-11",
    items: [
      { audience: "all", text: "Öğretmenin Bugün ekranında haftalık özet var: bu hafta kaç dersin tamamlandığı, kaçının yoklamasının eksik olduğu ve haftanın ödenip ödenmediği görünür." },
      { audience: "all", text: "Haftalık özeti açınca haftanın dersleri yoklama durumlarıyla listelenir; eksik yoklamayı girmek ya da \"Geldi\"yi \"Gelmedi\" veya \"Mazeretli\" yapmak tek dokunuş." },
      { audience: "all", text: "Yoklamada \"Geldi + önceki dersten devam\" düğmesi geçen dersin çalışmasını, ödevini ve hedefini hazır getirir; Kaydet telefonda hep ekranın altında durur." },
      { audience: "all", text: "Yarım bıraktığın ders notu sayfa yenilense de kaybolmaz; ders kartından veliyi tek dokunuşla arayabilir ya da WhatsApp'tan yazabilirsin." },
      { audience: "all", text: "Bildirimlerdeki \"öğrenci geldi mi?\" sorusuna \"Mazeretli\" cevabı eklendi; giriş ekranı en son kullandığın rolle açılır." },
      { audience: "all", text: "Abdera'yı telefonunun ana ekranına ekleyebilirsin: tarayıcı menüsünden \"Ana Ekrana Ekle\" dediğinde uygulama gibi tam ekran açılır." },
    ],
  },
  {
    version: "2026.10.11",
    date: "2026-10-11",
    items: [
      { audience: "all", text: "Yanlış girilen yoklamayı artık düzeltebilirsin: Bugün ekranındaki derste \"Düzelt\"e ya da takvimde derse dokun. \"Mazeretli\"ye çevrilen derse telafi hakkı açılır." },
      { audience: "all", text: "Henüz başlamamış bir derse yoklama girilemez; öğrenci erken gelirse yoklama dersten 15 dakika önce açılır." },
      { audience: "all", text: "Takvimde dersi iptal ederken \"Veli iptal etti\" seçeneği var: veli 24 saatten önce haber verdiyse telafi hakkı kendiliğinden açılır, daha geç haber verdiyse açılmaz." },
      { audience: "all", text: "Öğrenci künyesinde ilgi gösterilmesi gereken öğrencinin nedenleri görünür: art arda \"gelemiyorum\" cevabı, bırakılan pratik günlüğü, düşen yetenek puanı." },
      { audience: "admin", text: "Ana ekrandaki \"İlgi gerektirebilecek öğrenciler\" listesi devamsızlığın yanında bu nedenleri ve 3 ay ya da daha uzun süredir gecikmiş aidatı da gösterir." },
      { audience: "admin", text: "Bir kursu sonlandırırken ayrılma nedeni soruluyor (taşındı, maddi nedenler, ilgisini kaybetti…); \"neden gidiyorlar\" sorusunun cevabı zamanla birikir." },
      { audience: "admin", text: "Banka ekranında eşleşmeyen bir havaleyi aidata sayarken artık kimlik yazmıyorsun: velinin açık aidatları listelenir, birini seçmen yeterli." },
      { audience: "admin", text: "Yeni öğrenci kaydında haftalık ders programı formdaki başlangıç tarihinden başlar." },
    ],
  },
  {
    version: "2026.10.10.2",
    date: "2026-10-10",
    items: [
      { audience: "all", text: "Sekmelerin ve butonların üzerine gelince imleç artık el işaretine dönüşüyor; neye tıklanabildiği daha net görünüyor." },
    ],
  },
  {
    version: "2026.10.10",
    date: "2026-10-10",
    items: [
      { audience: "all", text: "Menüde yeni \"Hata bildir\" sayfası: bir şey çalışmıyorsa ya da bir önerin varsa hangi sayfada olduğunu seçip kısaca anlat, gönder. Gerisini biz takip ediyoruz." },
      { audience: "admin", text: "Hata bildir sayfasındaki \"Gelen bildirimler\" sekmesinde gelen bildirimleri, göreve dönüşenleri ve reddedilenleri görürsün." },
    ],
  },
  {
    version: "2026.10.09.3",
    date: "2026-10-09",
    items: [
      { audience: "admin", text: "Gelir ve gider ekranında yeni Kârlılık sekmesi: bu ay cebinde ne kaldığını ve son 6 ayın gidişini tek grafikte görürsün; öğretmen ücretleri ve sabit giderler düşülmüş hâliyle." },
      { audience: "admin", text: "\"Bu ay ne yapmalı?\" bölümü okulun kendi verisine göre öneriler sunar, ayda bir de kısa bir yorum yazar. \"Düzeltilmesi gerekenler\" ise ücreti girilmemiş öğretmen, alınmamış yoklama gibi hesabı bozan eksikleri listeler." },
      { audience: "admin", text: "\"Fikirlerim\"e yeni öğretmen, grup dersi, yeni branş ya da indirim kampanyası gibi fikirlerini yaz; yazdıkça aylık kâra ne katacağı hesaplanır. Deneysel \"Hedef belirle\" penceresi bunun yerine kaldırıldı." },
    ],
  },
  {
    version: "2026.10.09.2",
    date: "2026-10-09",
    items: [
      { audience: "admin", text: "Yanlış tarihle girilmiş ödemelerin tarihi düzeltilebilir: Gelir ve gider > Gelirler'de \"Tarih düzelt\" ile ödemeleri seç, gerçek ödeme gününü gir. Geçen ay alınıp sonradan girilen paralar doğru ayın gelirine geçer; ödeme kaydı silinmez." },
      { audience: "admin", text: "Tahsilat defteri günlere göre açılıp kapanır; her günün başlığında toplam, ödeme sayısı ve gecikmiş/peşin ödemeler görünür. Kendi ayı, Gecikmiş ve Peşin süzgeçleriyle aradığın ödemeyi daha kolay bulursun." },
      { audience: "all", text: "Telefonda takvim, yoklama, öğrenci künyesi ve özet kartları ekrana sığar; yazılar ve tarih kutuları artık kesilmez ya da taşmaz." },
      { audience: "admin", text: "Mesaj Merkezi telefonda tablo yerine okunaklı bir liste olarak açılır. Talepler menüden kalktı; bekleyen talepler Bugün ekranındaki karttan ve zilden açılır." },
    ],
  },
  {
    version: "2026.10.09",
    date: "2026-10-09",
    items: [
      { audience: "admin", text: "Gelir ve gider ekranında Gelir kartının altında gelirin ne kadarının o ayın aidatı, ne kadarının peşin veya gecikmiş ödeme olduğu görünür; Aidatlar ekranındaki \"ödedi\" sayısıyla farkın nedeni artık açık." },
      { audience: "admin", text: "Gelir rakamlarına dokununca ayrıntılı döküm açılır: dönem, enstrüman ve ödeme yöntemine göre kırılım ve ödeme listesi. Döküm Excel'de açılan dosya olarak indirilebilir." },
      { audience: "admin", text: "Deneysel \"Hedef belirle\": aylık gelir hedefini gir, okulun kendi verisine göre tahsilat, indirim, enstrüman bazında yeni öğrenci ve fiyat adımlarıyla bir yol haritası önerilir." },
    ],
  },
  {
    version: "2026.10.08.6",
    date: "2026-10-08",
    items: [
      { audience: "all", text: "Bugün ekranındaki ders kartında notu girilmiş ders \"Not girildi\", yoklaması alınmış ders \"Yoklama alındı\" olarak işaretlenir; hangi derse not yazdığın tek bakışta görünür." },
      { audience: "all", text: "Notu girilmiş derse dokununca önce kaydedilen not açılır; aynı not yanlışlıkla ikinci kez kaydedilmez, istersen \"Ek not yaz\" ile yeni not ekleyebilirsin." },
    ],
  },
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
