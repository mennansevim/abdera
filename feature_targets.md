# Abdera — Feature Targets

Bu dosya, Abdera Müzik Okulu Yönetim Sistemi için yapılacak ürün geliştirmelerini
öncelik ve teslim sırasına göre tanımlar. Teknik mimarinin ayrıntısı için
[`docs/17-technical-architecture.md`](docs/17-technical-architecture.md), tamamlanan
ürün fazlarının geçmişi için [`docs/15-product-phases.md`](docs/15-product-phases.md)
okunmalıdır.

## Nasıl okunur?

| Etiket | Anlamı |
|---|---|
| `Tamamlandı` | Kod, API, yetki ve temel testleri mevcut. |
| `Kısmi` | Bir bölümü çalışıyor; aşağıdaki eksikler tamamlanmalı. |
| `Planlandı` | Ürün kararı verilmiş, geliştirme sırasını bekliyor. |
| `Blokeli` | Kullanıcı/sağlayıcı bilgisi olmadan tamamlanamaz. |

Bir faz; arayüz, API, veritabanı migration'ı, yetki kontrolü, audit kaydı ve testleri
birlikte geçmeden tamamlanmış sayılmaz. Küçük okul ölçeği korunur: yeni bir özellik
gerçek bir operasyon problemini çözmüyorsa mikroservis, ayrı kuyruk, Redis veya ağır
bir workflow altyapısı eklenmez.

## Mevcut temel

- Faz 0–6 kapsamı (auth, kişiler, takvim, devam/RSVP, aidat, mesajlaşma, operasyon ve
  sanal IBAN) uygulanmış durumdadır.
- Faz 7–11'in ürün ekranları, API'leri, migration'ları, rol kapsamları, audit kayıtları
  ve otomatik testleri tamamlandı; kritik üç rol akışı Playwright ile tekrarlanabilir.
- Yedeklemenin `Fake` sağlayıcısı development içindir. Gerçek SFTP aktarımı ve gerçek
  WhatsApp/banka sağlayıcısı doğrulaması production hazırlığı fazındadır.

---

## Faz 7 — Takvim ve ders planlama deneyimi

**Öncelik:** P0 · **Durum:** `Tamamlandı`

Takvim, okulun günlük iş akışının merkezi olmalı; yönetici saati elle hesaplamadan
ders ekleyebilmeli ve taşırken hedef saati görebilmelidir.

- [x] Boş bir zaman hücresine çift tıklayınca yeni ders penceresini aç.
- [x] Çift tıklanan gün ve saati formda başlangıç değeri olarak doldur.
- [x] Seçilen öğretmenin müsaitliklerini akıllı zamanlayıcıda öner; çakışan saatleri
      gösterme veya açıkça kullanılamaz olarak işaretle.
- [x] Sürükle-bırak sırasında kartın üzerinde hedef gün/saat hover etiketi göster.
- [x] Hedef saat çizgisini ders kartlarının üzerinde tut; z-index/layering hatasını
      önle ve arka saat satırlarını görünür bırak.
- [x] Ders detay ekranından öğretmen, öğrenci, gün, saat, süre ve durum güncellenebilsin.
- [x] `Yeni ders saati` gibi custom değerler için tarih/saat/süre inputları ver; yalnızca
      placeholder metni gösterme.
- [x] Takvimde öğretmen filtresi ile enstrüman filtresini birlikte çalıştır.
- [x] Tekrarlayan ders kuralı: haftada en fazla 4 ders; seri dersler varsayılan olarak
      aynı gün ve saatte üretilsin.
- [x] Telafi asistanında yalnızca kullanılabilir telafi hakkı bulunan öğrencileri listele.
- [x] “Uygun slot bul ve yerleştir” ile öğretmen ve öğrencinin ortak boşluklarını sırala.

**Kabul kriterleri**

1. Boş hücreye çift tıklama, seçilen gün/saatle açılan bir ders formu üretir.
2. Kart taşınırken kullanıcı en az dakika hassasiyetinde hedef zamanı görür; bırakma
   sonrası çakışma kontrolü yapılır.
3. Haftada 5. ders oluşturma veya aynı seri için farklı gün/saat kuralı sunucu tarafında
   reddedilir; yalnızca frontend kontrolüne güvenilmez.
4. Ders detayında yapılan değişiklik takvimde ve bildirim kuyruğunda tutarlı görünür.

---

## Faz 8 — Aidat ve dönem hesabı

**Öncelik:** P0 · **Durum:** `Tamamlandı`

Aidat ekranı yalnızca finansal dönemi anlatmalı; ders geçmişi ve telafi bilgisi finans
ekranına taşınmamalıdır.

- [x] Aidat listesini tek, sade dönem görünümünde birleştir.
- [x] Her satır/kartta yalnızca öğrenci, öğretmen, enstrüman, dönem, tutar, kalan,
      vade ve ödeme durumu göster.
- [x] Ders listesi ve telafi detayını aidat listesinden kaldır; bunları öğrenci/takvim
      ekranlarında bırak.
- [x] Öğrenci alanını yazdıkça tamamlayan autocomplete yap; isim, veli telefonu,
      öğretmen ve enstrümanla arama yapılabilsin.
- [x] Öğretmen ve enstrüman bazlı filtreleri birlikte destekle.
- [x] Seçilen öğrencinin ödenmiş dönemlerini ve ödeme tarih/tutar/yöntem geçmişini
      ayrı bir hesap panelinde göster.
- [x] Toplu tahsilat, tekil ödeme, kısmi ödeme ve ödeme düzeltme akışlarını aynı
      ödeme geçmişine yaz.
- [x] Demo ortamı için 20–30 karışık aidat kaydı üret: ödenmiş, açık, gecikmiş,
      kısmi ve farklı öğretmen/enstrüman dağılımları olsun.
- [x] Liste boşken nasıl aidat oluşturulacağını görünür bir “Dönem aidatı ekle”
      çağrısıyla anlat.

**Kabul kriterleri**

1. Kullanıcı bir öğrenciyi 2–3 karakter yazarak bulur; sonuçlarda öğretmen ve
   enstrüman bilgisi görünür.
2. Öğretmen veya enstrüman filtresi değişince toplamlar ve liste aynı sorguya göre
   güncellenir.
3. Aidat ekranında ders kartı, haftalık ders sayısı veya telafi hakkı gösterilmez.
4. Seed/demo verisiyle açık, gecikmiş, kısmi ve ödenmiş sekmelerinin hepsi dolu
   ve ödeme işlemleri kalıcıdır.

---

## Faz 9 — Öğrenci, öğretmen ve kurs kayıtları

**Öncelik:** P1 · **Durum:** `Tamamlandı`

Kayıt ilişkileri tek bir yerde anlaşılır olmalı; aynı kurs hem öğretmen sayfasından hem
   öğrenci akışından yönetilebilmelidir.

- [x] Öğretmen listesinde öğrenci sayısını göster ve satırı açınca öğrencilerini listele.
- [x] Öğretmen detayından “Yeni öğrenci ekle” akışını aç.
- [x] “Başka kurs ekle” ile yeni öğretmen + enstrüman seçimini aynı akışta sun.
- [x] Öğrenci ekleme/kurs bağlama formunda başarılı kayıttan sonra formu kapat veya
      temizle; başarı mesajı ve eski seçimler ekranda kalmasın.
- [x] Mevcut kursu kaldırma/değiştirme işlemi için açık bir işlem ve onay adımı ekle.
- [x] Öğretmen sayfası ile öğrenci sayfasındaki enrollment bilgileri aynı API kaynağını
      kullansın.

**Kabul kriterleri**

1. Öğretmen satırında toplam öğrenci sayısı ve açılabilir öğrenci listesi görünür.
2. Yeni enrollment sonrası form kapanır/temizlenir; yinelenen kayıt oluşmaz.
3. Kurs silme yalnızca ilgili enrollment'ı etkiler; öğrenci veya öğretmen kaydını
   yanlışlıkla silmez.

---

## Faz 10 — Gelişim, eser ve veliye sunum

**Öncelik:** P1 · **Durum:** `Tamamlandı`

Öğrencinin hangi eser üzerinde çalıştığı tek bakışta anlaşılmalı; öğretmen notu veliye
   aktarılırken yapıcı ve ölçülebilir kalmalıdır.

- [x] Öğrenci gelişim ekranında çalışılan eserleri kronolojik listele.
- [x] Eser kaydına besteci, enstrüman, seviye, durum, hedef tarih ve nota/PDF/link ekle.
- [x] Zorluk derecesini 1–5 veya Başlangıç/Orta/İleri olarak standardize et.
- [x] Öğretmen kısa not girince isteğe bağlı “yapıcı metne dönüştür” önizlemesi sun;
      ham notu veliye otomatik göndermeden önce öğretmen onayı iste.
      *(OpenAI uyumlu sağlayıcı; `Ai__Provider=OpenAi` + `Ai__ApiKey` ile açılır. Öneri
      kaydedilmez/onaylanmaz, yalnızca önizlemeye düşer ve geri alınabilir.)*
- [x] Veli portalında eser, öğretmen yorumu ve sonraki hedefi ayrı bölümlerde göster.
- [x] AI sağlayıcısı yoksa mevcut metni bozma; özellik kapalıyken manuel düzenleme
      akışı eksiksiz çalışmaya devam et.

**Kabul kriterleri**

1. Bir öğrenci için eser listesi öğretmen, enstrüman, seviye ve son çalışma tarihiyle
   filtrelenebilir.
2. Veli yalnızca kendi çocuğunun onaylanmış yorumunu görür; ham öğretmen notu gizlidir.
3. AI dönüşümü geri alınabilir ve her değişiklik audit kaydı bırakır.

---

## Faz 11 — Hafif bağlılık özellikleri

**Öncelik:** P2 · **Durum:** `Tamamlandı`

Bu faz, temel operasyonlar oturduktan sonra küçük ve anlaşılır eklentilerle ürünü
   zenginleştirir; ayrı bir oyun veya karmaşık puan ekonomisi kurulmaz.

- [x] Enstrümana göre bakım hatırlatmaları: keman teli, piyano akordu vb. için okulun
      belirlediği periyot ve veliye bildirim.
- [x] Dijital pratik günlüğü: süre/hedef girişi, veli onayı ve birkaç basit rozet.
- [x] Repertuvar arşivinde nota PDF/link erişimi ve veli görünürlüğü.
- [x] Açıklanabilir devamsızlık sinyalinden yöneticiye “ilgi gerektiren öğrenci”
      uyarısı üret; otomatik “ayrılacak” kararı verme.

**Kabul kriterleri**

1. Her özellik kapatılabilir; kapalıyken mevcut ders, aidat ve veli akışı bozulmaz.
2. Bildirimler okul ayarlarına ve veli rızasına bağlıdır.
3. Risk uyarısı kullandığı devamsızlık eşiğini ve gözlem penceresini açıkça gösterir;
   AI veya otomatik karar tek başına işlem yapmaz.

---

## Faz 12 — Production hazırlığı ve kalite kapısı

**Öncelik:** P0 · **Durum:** `Kısmi / Blokeli` — kod tarafı tamam, yalnızca gerçek
sağlayıcı kimlik bilgileriyle yapılabilecek **canlı doğrulamalar** açık.

Özelliklerin canlıda güvenilir çalışması, yeni özellik eklemek kadar önemlidir.

- [ ] Gerçek SFTP sunucusuna şifreli yedek aktarımını doğrula.
- [x] En az bir yedek dosyasını ayrı boş veritabanına geri yükleyerek prova et.
- [ ] Gerçek WhatsApp sağlayıcısı, template onayı ve webhook imza doğrulamasını tamamla.
- [ ] Gerçek banka sağlayıcısı seçildikten sonra IBAN/webhook entegrasyonunu canlı
      sandbox'ta doğrula.
- [x] Üç kritik browser E2E akışı ekle: yönetici, öğretmen, veli.
- [x] Aidat, takvim, ödeme ve veli veri izolasyonu için smoke testleri CI'a koy.
- [x] `/health`, `/api/system/health`, backup freshness ve hata alarmını izlemeye al.
- [x] Production'da Fake sağlayıcıları, dev simülatörlerini ve varsayılan secret'ları
      kapat.
- [x] Uygulamanın `ASPNETCORE_ENVIRONMENT=Production` ile gerçekten ayağa kalktığını
      doğrula. *(Daha önce imkânsızdı: `ProductionSecretsGuard` `Banking__Provider`'ın
      `Fake` olmamasını şart koşarken `Program.cs` `Fake` dışındaki her değere `throw`
      ediyordu. `Banking__Provider=Manual` modu eklendi; iki taraf artık tek kaynağı
      (`BankingProviderModes`) kullanıyor ve `BankingProviderModesTests` tutarlılığı
      bekçiliyor.)*
- [x] HTTPS/reverse proxy: `docker compose --profile prod up -d` ile Caddy otomatik
      Let's Encrypt sertifikası alır; backend `UseForwardedHeaders` ile gerçek istemci
      IP'sini ve şemasını görür. *(Cookie'ler production'da `Secure=Always` olduğu için
      TLS opsiyonel değil - düz HTTP'de giriş sessizce çalışmıyordu.)*
- [x] Veli veri izolasyonu için ayrı, bağımsız bir browser smoke testi
      (`frontend/e2e/data-isolation.spec.ts`).

**Kabul kriterleri**

1. Son başarılı yedek ve geri yükleme provası tarih/saat ile kayıtlıdır.
2. Üç rolün temel akışları temiz bir veritabanında CI veya tekrarlanabilir smoke test
   ile çalışır.
3. Gerçek sağlayıcı başarısızlığında kullanıcıya anlaşılır hata, yöneticiyse audit ve
   alarm kaydı gösterilir.

## Önerilen uygulama sırası

1. **Faz 7:** Takvimde çift tıklama, hover hedef zamanı ve ders detay düzenleme.
2. **Faz 8:** Aidat ekranını tek dönem görünümüne indirme ve demo verisiyle doğrulama.
3. **Faz 9:** Enrollment akışlarını temizleme ve öğretmen → öğrenci görünümünü tamamlama.
4. **Faz 10:** Eser arşivi ve öğretmen gelişim yorumlarının veliye güvenli aktarımı.
5. **Faz 12:** Canlıya çıkış öncesi yedek, sağlayıcı ve E2E kalite kapısı.
6. **Faz 11:** Temel operasyonlar istikrarlı olduktan sonra bağlılık özellikleri.

## Her özellik için teslim şablonu

Her yeni iş kalemi şu sırayla kapatılır:

1. Kullanıcı akışı ve boş/hata durumları tasarlanır.
2. Domain kuralı ve yetki sınırı backend'de uygulanır.
3. Gerekli migration ve audit kaydı eklenir.
4. Unit + integration testi yazılır; kritik akış için browser smoke testi eklenir.
5. Demo verisiyle gerçek Compose ortamında doğrulanır.
6. README, API dokümantasyonu ve bu dosyadaki durum güncellenir.
